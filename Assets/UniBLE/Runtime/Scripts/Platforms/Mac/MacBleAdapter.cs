#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AOT;

namespace UniBLE.Platforms.Mac
{
    /// <summary>
    /// macOS BLE adapter implementation using CoreBluetooth
    /// </summary>
    public class MacBleAdapter : IBleAdapter
    {
        private static MacBleAdapter _instance;
        private readonly Dictionary<string, MacBleDevice> _discoveredDevices = new Dictionary<string, MacBleDevice>();
        private Action<IBleDevice> _onDeviceDiscovered;
        private BleAdapterState _state = BleAdapterState.Unknown;
        private bool _isScanning;
        private TaskCompletionSource<bool> _stateReadyTcs;

        public BleAdapterState State => _state;
        public event Action<BleAdapterState> OnStateChanged;

        #region Native Methods
        [DllImport("UniBlePlugin")]
        private static extern void UniBle_Initialize(StateChangedCallback stateCallback, DeviceDiscoveredCallback deviceCallback, DisconnectCallback disconnectCallback);

        [DllImport("UniBlePlugin")]
        private static extern bool UniBle_IsAvailable();

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_StartScan(string serviceUuidsJson);

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_StopScan();
        #endregion

        #region Callbacks
        private delegate void StateChangedCallback(int state);
        private delegate void DeviceDiscoveredCallback(IntPtr deviceId, IntPtr deviceName, IntPtr serviceUuidsJson);
        internal delegate void DisconnectCallback(string deviceId, string error);

        private static StateChangedCallback _stateChangedCallback;
        private static DeviceDiscoveredCallback _deviceDiscoveredCallback;
        private static DisconnectCallback _disconnectCallback;
        #endregion

        private static string PtrToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            return Marshal.PtrToStringAnsi(ptr);
        }

        public MacBleAdapter()
        {
            UnityEngine.Debug.Log("[UniBLE] MacBleAdapter constructor start");
            _instance = this;
            _stateReadyTcs = new TaskCompletionSource<bool>();
            _stateChangedCallback = OnNativeStateChanged;
            _deviceDiscoveredCallback = OnNativeDeviceDiscovered;
            _disconnectCallback = MacBleDevice.OnGlobalDisconnect;
            UnityEngine.Debug.Log("[UniBLE] Calling UniBle_Initialize...");
            UniBle_Initialize(_stateChangedCallback, _deviceDiscoveredCallback, _disconnectCallback);
            UnityEngine.Debug.Log("[UniBLE] UniBle_Initialize returned");
        }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            // Wait for the state to be determined (not Unknown)
            if (_state == BleAdapterState.Unknown)
            {
                using (cancellationToken.Register(() => _stateReadyTcs.TrySetCanceled()))
                {
                    await _stateReadyTcs.Task;
                }
            }
            return _state == BleAdapterState.PoweredOn;
        }

        public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
        {
            // macOS doesn't require explicit permission request for BLE
            return Task.FromResult(true);
        }

        public Task StartScanAsync(
            IEnumerable<string> serviceUuids,
            Action<IBleDevice> onDeviceDiscovered,
            CancellationToken cancellationToken = default)
        {
            if (_isScanning) return Task.CompletedTask;

            _onDeviceDiscovered = onDeviceDiscovered;
            _discoveredDevices.Clear();
            _isScanning = true;

            string uuidsJson = null;
            if (serviceUuids != null)
            {
                var list = new List<string>(serviceUuids);
                if (list.Count > 0)
                {
                    uuidsJson = "[" + string.Join(",", list.ConvertAll(u => $"\"{u}\"")) + "]";
                }
            }

            UnityEngine.Debug.Log($"[UniBLE] Before UniBle_StartScan, uuidsJson: {uuidsJson ?? "null"}");
            try
            {
                UniBle_StartScan(uuidsJson);
                UnityEngine.Debug.Log("[UniBLE] After UniBle_StartScan - OK");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[UniBLE] UniBle_StartScan exception: {e}");
            }

            cancellationToken.Register(() => StopScanAsync());

            return Task.CompletedTask;
        }

        public Task StopScanAsync(CancellationToken cancellationToken = default)
        {
            if (!_isScanning) return Task.CompletedTask;

            _isScanning = false;
            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_StopScan();
            });

            return Task.CompletedTask;
        }

        public Task<IBleDevice> ConnectToKnownDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (_discoveredDevices.TryGetValue(deviceId, out var device))
            {
                return Task.FromResult<IBleDevice>(device);
            }

            // Create a new device for known device connection
            var newDevice = new MacBleDevice(deviceId, "Unknown");
            _discoveredDevices[deviceId] = newDevice;
            return Task.FromResult<IBleDevice>(newDevice);
        }

        [MonoPInvokeCallback(typeof(StateChangedCallback))]
        private static void OnNativeStateChanged(int state)
        {
            UnityEngine.Debug.Log($"[UniBLE] OnNativeStateChanged called with state: {state}");
            MainThreadDispatcher.Enqueue(() =>
            {
                UnityEngine.Debug.Log($"[UniBLE] OnNativeStateChanged MainThread, _instance null: {_instance == null}");
                if (_instance == null) return;

                _instance._state = state switch
                {
                    0 => BleAdapterState.Unknown,
                    1 => BleAdapterState.Unsupported,
                    2 => BleAdapterState.Unauthorized,
                    3 => BleAdapterState.PoweredOff,
                    4 => BleAdapterState.PoweredOn,
                    _ => BleAdapterState.Unknown
                };

                UnityEngine.Debug.Log($"[UniBLE] State set to: {_instance._state}");

                // Signal that state is now determined
                if (_instance._state != BleAdapterState.Unknown)
                {
                    UnityEngine.Debug.Log("[UniBLE] Setting _stateReadyTcs result");
                    _instance._stateReadyTcs?.TrySetResult(true);
                }

                _instance.OnStateChanged?.Invoke(_instance._state);
            });
        }

        [MonoPInvokeCallback(typeof(DeviceDiscoveredCallback))]
        private static void OnNativeDeviceDiscovered(IntPtr deviceIdPtr, IntPtr deviceNamePtr, IntPtr serviceUuidsJsonPtr)
        {
            try
            {
                var deviceId = PtrToString(deviceIdPtr);
                var deviceName = PtrToString(deviceNamePtr);
                var serviceUuidsJson = PtrToString(serviceUuidsJsonPtr);
                UnityEngine.Debug.Log($"[UniBLE] Discovered: {deviceName} ({deviceId})");
                UnityEngine.Debug.Log($"[UniBLE] Advertised Services: {serviceUuidsJson ?? "none"}");

                MainThreadDispatcher.Enqueue(() =>
                {
                    if (_instance == null) return;

                    if (!_instance._discoveredDevices.ContainsKey(deviceId))
                    {
                        var device = new MacBleDevice(deviceId, deviceName);
                        _instance._discoveredDevices[deviceId] = device;
                        _instance._onDeviceDiscovered?.Invoke(device);
                    }
                });
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[UniBLE] OnNativeDeviceDiscovered exception: {e}");
            }
        }
    }
}
#endif
