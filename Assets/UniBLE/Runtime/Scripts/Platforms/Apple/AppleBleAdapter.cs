#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_IOS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AOT;

namespace UniBLE.Platforms.Apple
{
    /// <summary>
    /// Apple (macOS/iOS) BLE adapter implementation using CoreBluetooth
    /// </summary>
    public class AppleBleAdapter : IBleAdapter
    {
        private static AppleBleAdapter _instance;
        private readonly Dictionary<string, AppleBleDevice> _discoveredDevices = new Dictionary<string, AppleBleDevice>();
        private Action<IBleDevice> _onDeviceDiscovered;
        private BleAdapterState _state = BleAdapterState.Unknown;
        private bool _isScanning;
        private TaskCompletionSource<bool> _stateReadyTcs;

        public BleAdapterState State => _state;
        public event Action<BleAdapterState> OnStateChanged;

#if UNITY_IOS && !UNITY_EDITOR
        private const string DllName = "__Internal";
#else
        private const string DllName = "UniBlePlugin";
#endif

        #region Native Methods
        [DllImport(DllName)]
        private static extern void UniBle_SetDebugEnabled(bool enabled);

        [DllImport(DllName)]
        private static extern void UniBle_Initialize(StateChangedCallback stateCallback, DeviceDiscoveredCallback deviceCallback, DisconnectCallback disconnectCallback);

        [DllImport(DllName)]
        private static extern bool UniBle_IsAvailable();

        [DllImport(DllName)]
        private static extern void UniBle_StartScan(string serviceUuidsJson);

        [DllImport(DllName)]
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

        private static void Log(string message)
        {
            if (BleManager.DebugLogging)
            {
                UnityEngine.Debug.Log($"[UniBLE] {message}");
            }
        }

        public AppleBleAdapter()
        {
            Log("AppleBleAdapter constructor start");
            _instance = this;
            _stateReadyTcs = new TaskCompletionSource<bool>();
            _stateChangedCallback = OnNativeStateChanged;
            _deviceDiscoveredCallback = OnNativeDeviceDiscovered;
            _disconnectCallback = AppleBleDevice.OnGlobalDisconnect;
            UniBle_SetDebugEnabled(BleManager.DebugLogging);
            Log("Calling UniBle_Initialize...");
            UniBle_Initialize(_stateChangedCallback, _deviceDiscoveredCallback, _disconnectCallback);
            Log("UniBle_Initialize returned");
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
#if UNITY_IOS && !UNITY_EDITOR
            // iOS requires Bluetooth permission (handled by Info.plist)
            // The system will prompt automatically when BLE is accessed
            return Task.FromResult(true);
#else
            // macOS doesn't require explicit permission request for BLE
            return Task.FromResult(true);
#endif
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

            // Sync debug flag to native side in case it changed
            UniBle_SetDebugEnabled(BleManager.DebugLogging);

            string uuidsJson = null;
            if (serviceUuids != null)
            {
                var list = new List<string>(serviceUuids);
                if (list.Count > 0)
                {
                    uuidsJson = "[" + string.Join(",", list.ConvertAll(u => $"\"{u}\"")) + "]";
                }
            }

            Log($"Before UniBle_StartScan, uuidsJson: {uuidsJson ?? "null"}");
            try
            {
                UniBle_StartScan(uuidsJson);
                Log("After UniBle_StartScan - OK");
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
            var newDevice = new AppleBleDevice(deviceId, "Unknown");
            _discoveredDevices[deviceId] = newDevice;
            return Task.FromResult<IBleDevice>(newDevice);
        }

        [MonoPInvokeCallback(typeof(StateChangedCallback))]
        private static void OnNativeStateChanged(int state)
        {
            Log($"OnNativeStateChanged called with state: {state}");
            MainThreadDispatcher.Enqueue(() =>
            {
                Log($"OnNativeStateChanged MainThread, _instance null: {_instance == null}");
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

                Log($"State set to: {_instance._state}");

                // Signal that state is now determined
                if (_instance._state != BleAdapterState.Unknown)
                {
                    Log("Setting _stateReadyTcs result");
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
                Log($"Discovered: {deviceName} ({deviceId})");
                Log($"Advertised Services: {serviceUuidsJson ?? "none"}");

                MainThreadDispatcher.Enqueue(() =>
                {
                    if (_instance == null) return;

                    if (!_instance._discoveredDevices.ContainsKey(deviceId))
                    {
                        var device = new AppleBleDevice(deviceId, deviceName);
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
