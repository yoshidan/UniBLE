#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

namespace UniBLE.Platforms.Android
{
    /// <summary>
    /// Android BLE adapter implementation
    /// </summary>
    public class AndroidBleAdapter : IBleAdapter
    {
        private readonly AndroidJavaObject _plugin;
        private readonly Dictionary<string, AndroidBleDevice> _discoveredDevices = new Dictionary<string, AndroidBleDevice>();
        private readonly Dictionary<string, AndroidBleDevice> _activeDevices = new Dictionary<string, AndroidBleDevice>();
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

        // Bug 1: Use Unity's Permission API instead of Java's ActivityCompat
        public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<bool>();

            cancellationToken.Register(() => tcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                var permissions = new List<string>();

                int apiLevel = GetApiLevel();
                if (apiLevel >= 31) // Android 12 (S)
                {
                    if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN"))
                        permissions.Add("android.permission.BLUETOOTH_SCAN");
                    if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT"))
                        permissions.Add("android.permission.BLUETOOTH_CONNECT");
                }
                else
                {
                    if (!Permission.HasUserAuthorizedPermission("android.permission.ACCESS_FINE_LOCATION"))
                        permissions.Add("android.permission.ACCESS_FINE_LOCATION");
                }

                if (permissions.Count == 0)
                {
                    tcs.TrySetResult(true);
                    return;
                }

                var callbacks = new PermissionCallbacks();
                int remaining = permissions.Count;
                bool allGranted = true;

                callbacks.PermissionGranted += (perm) =>
                {
                    remaining--;
                    if (remaining <= 0)
                        tcs.TrySetResult(allGranted);
                };
                callbacks.PermissionDenied += (perm) =>
                {
                    allGranted = false;
                    remaining--;
                    if (remaining <= 0)
                        tcs.TrySetResult(false);
                };
                callbacks.PermissionDeniedAndDontAskAgain += (perm) =>
                {
                    allGranted = false;
                    remaining--;
                    if (remaining <= 0)
                        tcs.TrySetResult(false);
                };

                Permission.RequestUserPermissions(permissions.ToArray(), callbacks);
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
                if (_activeDevices.TryGetValue(deviceId, out var existingDevice))
                {
                    tcs.TrySetResult(existingDevice);
                    return;
                }

                var androidDevice = _plugin.Call<AndroidJavaObject>("getDevice", deviceId);
                string name = "";
                if (androidDevice != null)
                {
                    name = androidDevice.Call<string>("getName") ?? "";
                }

                var device = new AndroidBleDevice(deviceId, name, _plugin);
                _activeDevices[deviceId] = device;
                tcs.TrySetResult(device);
            });

            return tcs.Task;
        }

        internal void OnDeviceDiscovered(string deviceId, string deviceName)
        {
            if (!_discoveredDevices.TryGetValue(deviceId, out var device))
            {
                if (!_activeDevices.TryGetValue(deviceId, out device))
                {
                    device = new AndroidBleDevice(deviceId, deviceName, _plugin);
                    _activeDevices[deviceId] = device;
                }
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

        // Bug 2: handle post-connection disconnect notifications from Java
        internal void OnDeviceDisconnected(string deviceId, string error)
        {
            if (_activeDevices.TryGetValue(deviceId, out var device))
            {
                device.OnDisconnected(error);
            }
        }

        private static int GetApiLevel()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                return version.GetStatic<int>("SDK_INT");
            }
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

            // Called from Java (Bug 2: disconnect notification)
            public void onDeviceDisconnected(string deviceId, string error)
            {
                MainThreadDispatcher.Enqueue(() =>
                {
                    _adapter.OnDeviceDisconnected(deviceId, error);
                });
            }
        }
    }
}
#endif
