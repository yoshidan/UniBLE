#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UniBLE.Platforms.Android
{
    /// <summary>
    /// Android BLE adapter implementation
    /// </summary>
    public class AndroidBleAdapter : IBleAdapter
    {
        private readonly AndroidJavaObject _plugin;
        private readonly Dictionary<string, AndroidBleDevice> _discoveredDevices = new Dictionary<string, AndroidBleDevice>();
        private Action<IBleDevice> _onDeviceDiscovered;
        private BleAdapterState _state = BleAdapterState.Unknown;
        private bool _isScanning;

        public BleAdapterState State => _state;
        public event Action<BleAdapterState> OnStateChanged;

        public AndroidBleAdapter()
        {
            using (var pluginClass = new AndroidJavaClass("com.unible.UniBlePlugin"))
            {
                _plugin = pluginClass.CallStatic<AndroidJavaObject>("getInstance");
            }

            // Get current Activity from Unity
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                _plugin.Call("initialize", activity, new BleCallback(this));
            }
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            var result = _plugin.Call<bool>("isAvailable");
            return Task.FromResult(result);
        }

        public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<bool>();

            cancellationToken.Register(() => tcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                _plugin.Call("requestPermissions", new PermissionCallback(granted =>
                {
                    tcs.TrySetResult(granted);
                }));
            });

            return tcs.Task;
        }

        public Task StartScanAsync(
            IEnumerable<string> serviceUuids,
            Action<IBleDevice> onDeviceDiscovered,
            CancellationToken cancellationToken = default)
        {
            if (_isScanning)
            {
                return Task.CompletedTask;
            }

            _onDeviceDiscovered = onDeviceDiscovered;
            _discoveredDevices.Clear();
            _isScanning = true;

            string[] uuidArray = null;
            if (serviceUuids != null)
            {
                var list = new List<string>(serviceUuids);
                uuidArray = list.ToArray();
            }

            MainThreadDispatcher.Enqueue(() =>
            {
                _plugin.Call("startScan", uuidArray);
            });

            cancellationToken.Register(() =>
            {
                StopScanAsync();
            });

            return Task.CompletedTask;
        }

        public Task StopScanAsync(CancellationToken cancellationToken = default)
        {
            if (!_isScanning)
            {
                return Task.CompletedTask;
            }

            _isScanning = false;
            MainThreadDispatcher.Enqueue(() =>
            {
                _plugin.Call("stopScan");
            });

            return Task.CompletedTask;
        }

        public Task<IBleDevice> ConnectToKnownDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<IBleDevice>();

            cancellationToken.Register(() => tcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                var androidDevice = _plugin.Call<AndroidJavaObject>("getDevice", deviceId);
                if (androidDevice == null)
                {
                    tcs.TrySetException(new BleException(BleErrorCode.ConnectionFailed, $"Device not found: {deviceId}"));
                    return;
                }

                var device = new AndroidBleDevice(deviceId, androidDevice.Call<string>("getName"), _plugin);
                tcs.TrySetResult(device);
            });

            return tcs.Task;
        }

        internal void OnDeviceDiscovered(string deviceId, string deviceName)
        {
            if (!_discoveredDevices.TryGetValue(deviceId, out var device))
            {
                device = new AndroidBleDevice(deviceId, deviceName, _plugin);
                _discoveredDevices[deviceId] = device;
                _onDeviceDiscovered?.Invoke(device);
            }
        }

        internal void OnAdapterStateChanged(int state)
        {
            _state = state switch
            {
                0 => BleAdapterState.Unknown,
                1 => BleAdapterState.Unsupported,
                2 => BleAdapterState.Unauthorized,
                3 => BleAdapterState.PoweredOff,
                4 => BleAdapterState.PoweredOn,
                _ => BleAdapterState.Unknown
            };
            OnStateChanged?.Invoke(_state);
        }

        /// <summary>
        /// Callback class for BLE events from Java
        /// </summary>
        private class BleCallback : AndroidJavaProxy
        {
            private readonly AndroidBleAdapter _adapter;

            public BleCallback(AndroidBleAdapter adapter) : base("com.unible.UniBleCallback")
            {
                _adapter = adapter;
            }

            // Called from Java
            public void onDeviceDiscovered(string deviceId, string deviceName, AndroidJavaObject device)
            {
                MainThreadDispatcher.Enqueue(() =>
                {
                    _adapter.OnDeviceDiscovered(deviceId, deviceName);
                });
            }

            // Called from Java
            public void onStateChanged(int state)
            {
                MainThreadDispatcher.Enqueue(() =>
                {
                    _adapter.OnAdapterStateChanged(state);
                });
            }
        }

        /// <summary>
        /// Callback class for permission request
        /// </summary>
        private class PermissionCallback : AndroidJavaProxy
        {
            private readonly Action<bool> _callback;

            public PermissionCallback(Action<bool> callback) : base("com.unible.PermissionCallback")
            {
                _callback = callback;
            }

            // Called from Java
            public void onResult(bool granted)
            {
                MainThreadDispatcher.Enqueue(() =>
                {
                    _callback?.Invoke(granted);
                });
            }
        }
    }
}
#endif
