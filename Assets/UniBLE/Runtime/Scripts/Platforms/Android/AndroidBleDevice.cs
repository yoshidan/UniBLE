#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UniBLE.Platforms.Android
{
    /// <summary>
    /// Android BLE device implementation
    /// </summary>
    public class AndroidBleDevice : IBleDevice
    {
        private readonly AndroidJavaObject _plugin;
        private readonly Dictionary<string, AndroidBleService> _services = new Dictionary<string, AndroidBleService>();
        private BleConnectionState _connectionState = BleConnectionState.Disconnected;
        private TaskCompletionSource<bool> _connectTcs;
        private TaskCompletionSource<IReadOnlyList<IBleService>> _discoverServicesTcs;
        private TaskCompletionSource<bool> _disconnectTcs;

        public string Id { get; }
        public string Name { get; }
        public BleConnectionState ConnectionState => _connectionState;
        public event Action<BleConnectionState> OnConnectionStateChanged;

        internal AndroidBleDevice(string id, string name, AndroidJavaObject plugin)
        {
            Id = id;
            Name = name ?? "Unknown";
            _plugin = plugin;
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
                _plugin.Call("connect", Id, new ConnectionCallback(this));
            });

            return _connectTcs.Task;
        }

        // Bug 2: truly async disconnect via TaskCompletionSource
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_connectionState != BleConnectionState.Connected)
            {
                return Task.CompletedTask;
            }

            _disconnectTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _disconnectTcs.TrySetCanceled());

            _connectionState = BleConnectionState.Disconnecting;
            OnConnectionStateChanged?.Invoke(_connectionState);

            MainThreadDispatcher.Enqueue(() =>
            {
                _plugin.Call("disconnect", Id);
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
                _plugin.Call("discoverServices", Id, new ServiceDiscoveryCallback(this));
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

        internal void OnConnected()
        {
            _connectionState = BleConnectionState.Connected;
            OnConnectionStateChanged?.Invoke(_connectionState);
            _connectTcs?.TrySetResult(true);
        }

        internal void OnConnectionFailed(string error)
        {
            _connectionState = BleConnectionState.Disconnected;
            OnConnectionStateChanged?.Invoke(_connectionState);
            _connectTcs?.TrySetException(new BleException(BleErrorCode.ConnectionFailed, error));
        }

        // Bug 2: called from adapter's onDeviceDisconnected callback (or ConnectionCallback)
        internal void OnDisconnected(string error = null)
        {
            _connectionState = BleConnectionState.Disconnected;
            OnConnectionStateChanged?.Invoke(_connectionState);
            _services.Clear();

            if (_disconnectTcs != null)
            {
                if (error != null)
                {
                    _disconnectTcs.TrySetException(new BleException(BleErrorCode.ConnectionLost, error));
                }
                else
                {
                    _disconnectTcs.TrySetResult(true);
                }
                _disconnectTcs = null;
            }

            // Also fail any pending connect if the device disconnected unexpectedly during connection
            if (_connectTcs != null)
            {
                _connectTcs.TrySetException(new BleException(BleErrorCode.ConnectionLost, error ?? "Device disconnected"));
                _connectTcs = null;
            }
        }

        internal void OnServicesDiscovered(AndroidJavaObject[] services)
        {
            _services.Clear();
            var result = new List<IBleService>();

            if (services != null)
            {
                foreach (var service in services)
                {
                    var uuid = service.Call<AndroidJavaObject>("getUuid").Call<string>("toString");
                    var bleService = new AndroidBleService(uuid, _plugin, Id);
                    _services[uuid] = bleService;
                    result.Add(bleService);
                }
            }

            _discoverServicesTcs?.TrySetResult(result);
        }

        internal void OnServiceDiscoveryFailed(string error)
        {
            _discoverServicesTcs?.TrySetException(new BleException(BleErrorCode.ServiceNotFound, error));
        }

        /// <summary>
        /// Callback class for connection events
        /// </summary>
        private class ConnectionCallback : AndroidJavaProxy
        {
            private readonly AndroidBleDevice _device;

            public ConnectionCallback(AndroidBleDevice device) : base("com.unible.ConnectionCallback")
            {
                _device = device;
            }

            // Called from Java
            public void onConnected()
            {
                MainThreadDispatcher.Enqueue(() => _device.OnConnected());
            }

            // Called from Java
            public void onConnectionFailed(string error)
            {
                MainThreadDispatcher.Enqueue(() => _device.OnConnectionFailed(error));
            }

            // Called from Java (connection-phase disconnect)
            public void onDisconnected()
            {
                MainThreadDispatcher.Enqueue(() => _device.OnDisconnected());
            }
        }

        /// <summary>
        /// Callback class for service discovery events
        /// </summary>
        private class ServiceDiscoveryCallback : AndroidJavaProxy
        {
            private readonly AndroidBleDevice _device;

            public ServiceDiscoveryCallback(AndroidBleDevice device) : base("com.unible.ServiceDiscoveryCallback")
            {
                _device = device;
            }

            // Called from Java
            public void onServicesDiscovered(AndroidJavaObject[] services)
            {
                MainThreadDispatcher.Enqueue(() => _device.OnServicesDiscovered(services));
            }

            // Called from Java
            public void onServiceDiscoveryFailed(string error)
            {
                MainThreadDispatcher.Enqueue(() => _device.OnServiceDiscoveryFailed(error));
            }
        }
    }
}
#endif
