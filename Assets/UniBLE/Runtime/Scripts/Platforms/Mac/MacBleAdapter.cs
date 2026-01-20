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
        private static extern void UniBle_Initialize(StateChangedCallback stateCallback, DeviceDiscoveredCallback deviceCallback);

        [DllImport("UniBlePlugin")]
        private static extern bool UniBle_IsAvailable();

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_StartScan(string serviceUuidsJson);

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_StopScan();
        #endregion

        #region Callbacks
        private delegate void StateChangedCallback(int state);
        private delegate void DeviceDiscoveredCallback(IntPtr deviceId, IntPtr deviceName);

        private static StateChangedCallback _stateChangedCallback;
        private static DeviceDiscoveredCallback _deviceDiscoveredCallback;
        #endregion

        private static string PtrToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            return Marshal.PtrToStringAnsi(ptr);
        }

        public MacBleAdapter()
        {
            _instance = this;
            _stateReadyTcs = new TaskCompletionSource<bool>();
            _stateChangedCallback = OnNativeStateChanged;
            _deviceDiscoveredCallback = OnNativeDeviceDiscovered;
            UniBle_Initialize(_stateChangedCallback, _deviceDiscoveredCallback);
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
                uuidsJson = "[" + string.Join(",", list.ConvertAll(u => $"\"{u}\"")) + "]";
            }

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_StartScan(uuidsJson);
            });

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
            MainThreadDispatcher.Enqueue(() =>
            {
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

                // Signal that state is now determined
                if (_instance._state != BleAdapterState.Unknown)
                {
                    _instance._stateReadyTcs?.TrySetResult(true);
                }

                _instance.OnStateChanged?.Invoke(_instance._state);
            });
        }

        [MonoPInvokeCallback(typeof(DeviceDiscoveredCallback))]
        private static void OnNativeDeviceDiscovered(IntPtr deviceIdPtr, IntPtr deviceNamePtr)
        {
            var deviceId = PtrToString(deviceIdPtr);
            var deviceName = PtrToString(deviceNamePtr);

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
    }
}
#endif
