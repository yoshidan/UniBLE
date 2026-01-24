#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AOT;
using UnityEngine;

namespace UniBLE.Platforms.Mac
{
    /// <summary>
    /// macOS BLE device implementation
    /// </summary>
    public class MacBleDevice : IBleDevice
    {
        private static readonly Dictionary<string, MacBleDevice> _devices = new Dictionary<string, MacBleDevice>();
        private readonly Dictionary<string, MacBleService> _services = new Dictionary<string, MacBleService>();
        private BleConnectionState _connectionState = BleConnectionState.Disconnected;
        private TaskCompletionSource<bool> _connectTcs;
        private TaskCompletionSource<bool> _disconnectTcs;
        private TaskCompletionSource<IReadOnlyList<IBleService>> _discoverServicesTcs;

        public string Id { get; }
        public string Name { get; }
        public BleConnectionState ConnectionState => _connectionState;
        public event Action<BleConnectionState> OnConnectionStateChanged;

        #region Native Methods
        [DllImport("UniBlePlugin")]
        private static extern void UniBle_Connect(string deviceId, ConnectionCallback callback);

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_Disconnect(string deviceId, DisconnectCallback callback);

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_DiscoverServices(string deviceId, ServiceDiscoveryCallback callback);
        #endregion

        #region Callbacks
        private delegate void ConnectionCallback(string deviceId, bool success, string error);
        private delegate void DisconnectCallback(string deviceId, string error);
        private delegate void ServiceDiscoveryCallback(string deviceId, string servicesJson, string error);

        private static ConnectionCallback _connectionCallback;
        private static DisconnectCallback _disconnectCallback;
        private static ServiceDiscoveryCallback _serviceDiscoveryCallback;
        #endregion

        static MacBleDevice()
        {
            _connectionCallback = OnNativeConnectionResult;
            _disconnectCallback = OnNativeDisconnectResult;
            _serviceDiscoveryCallback = OnNativeServicesDiscovered;
        }

        internal MacBleDevice(string id, string name)
        {
            Id = id;
            Name = name ?? "Unknown";
            _devices[id] = this;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_connectionState == BleConnectionState.Connected)
            {
                return Task.CompletedTask;
            }

            _connectTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _connectTcs.TrySetCanceled());

            _connectionState = BleConnectionState.Connecting;
            OnConnectionStateChanged?.Invoke(_connectionState);

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_Connect(Id, _connectionCallback);
            });

            return _connectTcs.Task;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_connectionState == BleConnectionState.Disconnected)
            {
                return Task.CompletedTask;
            }

            _disconnectTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _disconnectTcs.TrySetCanceled());

            _connectionState = BleConnectionState.Disconnecting;
            OnConnectionStateChanged?.Invoke(_connectionState);

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_Disconnect(Id, _disconnectCallback);
            });

            return _disconnectTcs.Task;
        }

        public Task<IReadOnlyList<IBleService>> GetServicesAsync(CancellationToken cancellationToken = default)
        {
            if (_connectionState != BleConnectionState.Connected)
            {
                throw new BleException(BleErrorCode.NotConnected, "Device is not connected");
            }

            _discoverServicesTcs = new TaskCompletionSource<IReadOnlyList<IBleService>>();
            cancellationToken.Register(() => _discoverServicesTcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_DiscoverServices(Id, _serviceDiscoveryCallback);
            });

            return _discoverServicesTcs.Task;
        }

        public async Task<IBleService> GetServiceAsync(string uuid, CancellationToken cancellationToken = default)
        {
            var services = await GetServicesAsync(cancellationToken);
            foreach (var service in services)
            {
                if (service.Uuid.Equals(uuid, StringComparison.OrdinalIgnoreCase))
                {
                    return service;
                }
            }
            throw new BleException(BleErrorCode.ServiceNotFound, $"Service not found: {uuid}");
        }

        [MonoPInvokeCallback(typeof(ConnectionCallback))]
        private static void OnNativeConnectionResult(string deviceId, bool success, string error)
        {
            MainThreadDispatcher.Enqueue(() =>
            {
                if (!_devices.TryGetValue(deviceId, out var device)) return;

                if (success)
                {
                    device._connectionState = BleConnectionState.Connected;
                    device.OnConnectionStateChanged?.Invoke(device._connectionState);
                    device._connectTcs?.TrySetResult(true);
                }
                else
                {
                    device._connectionState = BleConnectionState.Disconnected;
                    device.OnConnectionStateChanged?.Invoke(device._connectionState);
                    device._connectTcs?.TrySetException(new BleException(BleErrorCode.ConnectionFailed, error ?? "Connection failed"));
                }
            });
        }

        [MonoPInvokeCallback(typeof(DisconnectCallback))]
        private static void OnNativeDisconnectResult(string deviceId, string error)
        {
            MainThreadDispatcher.Enqueue(() =>
            {
                if (!_devices.TryGetValue(deviceId, out var device)) return;

                device._connectionState = BleConnectionState.Disconnected;
                device.OnConnectionStateChanged?.Invoke(device._connectionState);
                device._services.Clear();

                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"[UniBLE] Disconnect error: {error}");
                }

                device._disconnectTcs?.TrySetResult(true);
            });
        }

        [MonoPInvokeCallback(typeof(ServiceDiscoveryCallback))]
        private static void OnNativeServicesDiscovered(string deviceId, string servicesJson, string error)
        {
            MainThreadDispatcher.Enqueue(() =>
            {
                if (!_devices.TryGetValue(deviceId, out var device)) return;

                if (!string.IsNullOrEmpty(error))
                {
                    device._discoverServicesTcs?.TrySetException(new BleException(BleErrorCode.ServiceNotFound, error));
                    return;
                }

                device._services.Clear();
                var result = new List<IBleService>();

                // Parse JSON array of service UUIDs: ["uuid1", "uuid2", ...]
                if (!string.IsNullOrEmpty(servicesJson))
                {
                    var uuids = ParseJsonStringArray(servicesJson);
                    foreach (var uuid in uuids)
                    {
                        var service = new MacBleService(uuid, deviceId);
                        device._services[uuid] = service;
                        result.Add(service);
                    }
                }

                device._discoverServicesTcs?.TrySetResult(result);
            });
        }

        private static List<string> ParseJsonStringArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json)) return result;

            // Simple JSON array parser for ["str1", "str2", ...]
            json = json.Trim();
            if (!json.StartsWith("[") || !json.EndsWith("]")) return result;

            json = json.Substring(1, json.Length - 2);
            var parts = json.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim().Trim('"');
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }
            return result;
        }
    }
}
#endif
