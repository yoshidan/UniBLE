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
    /// Apple (macOS/iOS) BLE characteristic implementation
    /// </summary>
    public class AppleBleCharacteristic : IBleCharacteristic
    {
        private static readonly Dictionary<string, AppleBleCharacteristic> _characteristics = new Dictionary<string, AppleBleCharacteristic>();

        private readonly string _deviceId;
        private readonly BleUuid _serviceUuid;
        private readonly IBleDispatcher _dispatcher;
        private TaskCompletionSource<byte[]> _readTcs;
        private TaskCompletionSource<bool> _writeTcs;
        private TaskCompletionSource<bool> _subscribeTcs;
        private TaskCompletionSource<bool> _unsubscribeTcs;

        public BleUuid Uuid { get; }
        public BleCharacteristicProperties Properties { get; }
        private Action<byte[]> _onNotify;

        #region Native Methods
        [DllImport(AppleNativeConstants.DllName)]
        private static extern void UniBle_ReadCharacteristic(string deviceId, string serviceUuid, string characteristicUuid, ReadCallback callback);

        [DllImport(AppleNativeConstants.DllName)]
        private static extern void UniBle_WriteCharacteristic(string deviceId, string serviceUuid, string characteristicUuid, byte[] data, int dataLength, bool withResponse, WriteCallback callback);

        [DllImport(AppleNativeConstants.DllName)]
        private static extern void UniBle_Subscribe(string deviceId, string serviceUuid, string characteristicUuid, NotifyCallback notifyCallback, SubscribeCallback resultCallback);

        [DllImport(AppleNativeConstants.DllName)]
        private static extern void UniBle_Unsubscribe(string deviceId, string serviceUuid, string characteristicUuid, SubscribeCallback resultCallback);
        #endregion

        #region Callbacks
        private delegate void ReadCallback(string deviceId, string serviceUuid, string characteristicUuid, IntPtr data, int dataLength, string error);
        private delegate void WriteCallback(string deviceId, string serviceUuid, string characteristicUuid, string error);
        private delegate void NotifyCallback(string deviceId, string serviceUuid, string characteristicUuid, IntPtr data, int dataLength);
        private delegate void SubscribeCallback(string deviceId, string serviceUuid, string characteristicUuid, string error);

        private static ReadCallback _readCallback;
        private static WriteCallback _writeCallback;
        private static NotifyCallback _notifyCallback;
        private static SubscribeCallback _subscribeCallback;
        private static SubscribeCallback _unsubscribeCallback;
        #endregion

        static AppleBleCharacteristic()
        {
            _readCallback = OnNativeReadResult;
            _writeCallback = OnNativeWriteResult;
            _notifyCallback = OnNativeNotify;
            _subscribeCallback = OnNativeSubscribeResult;
            _unsubscribeCallback = OnNativeUnsubscribeResult;
        }

        internal AppleBleCharacteristic(BleUuid uuid, BleCharacteristicProperties properties, string deviceId, BleUuid serviceUuid, IBleDispatcher dispatcher)
        {
            Uuid = uuid;
            Properties = properties;
            _deviceId = deviceId;
            _serviceUuid = serviceUuid;
            _dispatcher = dispatcher;
            var key = GetKey(deviceId, serviceUuid.ToFullString(), uuid.ToFullString());
            _characteristics[key] = this;
        }

        private static string GetKey(string deviceId, string serviceUuid, string characteristicUuid)
        {
            return $"{deviceId}:{serviceUuid}:{characteristicUuid}";
        }

        internal static void RemoveForDevice(string deviceId)
        {
            var keysToRemove = new List<string>();
            var prefix = deviceId + ":";
            foreach (var key in _characteristics.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    keysToRemove.Add(key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _characteristics.Remove(key);
            }
        }

        public Task<byte[]> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!Properties.HasFlag(BleCharacteristicProperties.Read))
            {
                throw new BleException(BleErrorCode.NotSupported, "Characteristic does not support reading");
            }

            _readTcs?.TrySetCanceled();
            _readTcs = new TaskCompletionSource<byte[]>();
            cancellationToken.Register(() => _readTcs.TrySetCanceled());

            _dispatcher.Dispatch(() =>
            {
                UniBle_ReadCharacteristic(_deviceId, _serviceUuid.ToFullString(), Uuid.ToFullString(), _readCallback);
            });

            return _readTcs.Task;
        }

        public Task WriteAsync(byte[] data, bool withResponse = true, CancellationToken cancellationToken = default)
        {
            var requiredProperty = withResponse ? BleCharacteristicProperties.Write : BleCharacteristicProperties.WriteWithoutResponse;
            if (!Properties.HasFlag(requiredProperty))
            {
                throw new BleException(BleErrorCode.NotSupported, $"Characteristic does not support {(withResponse ? "write" : "write without response")}");
            }

            _writeTcs?.TrySetCanceled();
            _writeTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _writeTcs.TrySetCanceled());

            _dispatcher.Dispatch(() =>
            {
                UniBle_WriteCharacteristic(_deviceId, _serviceUuid.ToFullString(), Uuid.ToFullString(), data, data.Length, withResponse, _writeCallback);
            });

            return _writeTcs.Task;
        }

        public Task SubscribeAsync(Action<byte[]> onNotify, CancellationToken cancellationToken = default)
        {
            if (!Properties.HasFlag(BleCharacteristicProperties.Notify) && !Properties.HasFlag(BleCharacteristicProperties.Indicate))
            {
                throw new BleException(BleErrorCode.NotSupported, "Characteristic does not support notifications");
            }

            _onNotify = onNotify;
            _subscribeTcs?.TrySetCanceled();
            _subscribeTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _subscribeTcs.TrySetCanceled());

            _dispatcher.Dispatch(() =>
            {
                UniBle_Subscribe(_deviceId, _serviceUuid.ToFullString(), Uuid.ToFullString(), _notifyCallback, _subscribeCallback);
            });

            return _subscribeTcs.Task;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            _onNotify = null;
            _unsubscribeTcs?.TrySetCanceled();
            _unsubscribeTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _unsubscribeTcs.TrySetCanceled());

            _dispatcher.Dispatch(() =>
            {
                UniBle_Unsubscribe(_deviceId, _serviceUuid.ToFullString(), Uuid.ToFullString(), _unsubscribeCallback);
            });

            return _unsubscribeTcs.Task;
        }

        [MonoPInvokeCallback(typeof(ReadCallback))]
        private static void OnNativeReadResult(string deviceId, string serviceUuid, string characteristicUuid, IntPtr data, int dataLength, string error)
        {
            byte[] dataArray = null;
            if (data != IntPtr.Zero && dataLength > 0)
            {
                dataArray = new byte[dataLength];
                Marshal.Copy(data, dataArray, 0, dataLength);
            }

            var key0 = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
            if (!_characteristics.TryGetValue(key0, out var c)) return;
            c._dispatcher.Dispatch(() =>
            {
                var key = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
                if (!_characteristics.TryGetValue(key, out var characteristic)) return;

                if (!string.IsNullOrEmpty(error))
                {
                    characteristic._readTcs?.TrySetException(new BleException(BleErrorCode.ReadFailed, error));
                    return;
                }

                characteristic._readTcs?.TrySetResult(dataArray ?? new byte[0]);
            });
        }

        [MonoPInvokeCallback(typeof(WriteCallback))]
        private static void OnNativeWriteResult(string deviceId, string serviceUuid, string characteristicUuid, string error)
        {
            var key0 = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
            if (!_characteristics.TryGetValue(key0, out var c)) return;
            c._dispatcher.Dispatch(() =>
            {
                var key = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
                if (!_characteristics.TryGetValue(key, out var characteristic)) return;

                if (!string.IsNullOrEmpty(error))
                {
                    characteristic._writeTcs?.TrySetException(new BleException(BleErrorCode.WriteFailed, error));
                    return;
                }

                characteristic._writeTcs?.TrySetResult(true);
            });
        }

        // Invoked directly (bypassing MainThreadDispatcher) to ensure delivery even when
        // Unity is paused (background). The native data is copied to a managed byte array
        // immediately so it remains valid after the native call returns.
        // Note: The callback fires on the CoreBluetooth dispatch queue thread, NOT the Unity
        // main thread. If the user needs Unity API access, they should use
        // the main thread dispatcher in their handler.
        [MonoPInvokeCallback(typeof(NotifyCallback))]
        private static void OnNativeNotify(string deviceId, string serviceUuid, string characteristicUuid, IntPtr data, int dataLength)
        {
            byte[] dataArray = null;
            if (data != IntPtr.Zero && dataLength > 0)
            {
                dataArray = new byte[dataLength];
                Marshal.Copy(data, dataArray, 0, dataLength);
            }

            var key = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
            if (!_characteristics.TryGetValue(key, out var characteristic)) return;
            characteristic._onNotify?.Invoke(dataArray ?? new byte[0]);
        }

        [MonoPInvokeCallback(typeof(SubscribeCallback))]
        private static void OnNativeSubscribeResult(string deviceId, string serviceUuid, string characteristicUuid, string error)
        {
            var key0 = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
            if (!_characteristics.TryGetValue(key0, out var c)) return;
            c._dispatcher.Dispatch(() =>
            {
                var key = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
                if (!_characteristics.TryGetValue(key, out var characteristic)) return;

                if (!string.IsNullOrEmpty(error))
                {
                    characteristic._subscribeTcs?.TrySetException(new BleException(BleErrorCode.NotificationFailed, error));
                    return;
                }

                characteristic._subscribeTcs?.TrySetResult(true);
            });
        }

        [MonoPInvokeCallback(typeof(SubscribeCallback))]
        private static void OnNativeUnsubscribeResult(string deviceId, string serviceUuid, string characteristicUuid, string error)
        {
            var key0 = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
            if (!_characteristics.TryGetValue(key0, out var c)) return;
            c._dispatcher.Dispatch(() =>
            {
                var key = GetKey(deviceId, new BleUuid(serviceUuid).ToFullString(), new BleUuid(characteristicUuid).ToFullString());
                if (!_characteristics.TryGetValue(key, out var characteristic)) return;

                if (!string.IsNullOrEmpty(error))
                {
                    characteristic._unsubscribeTcs?.TrySetException(new BleException(BleErrorCode.NotificationFailed, error));
                    return;
                }

                characteristic._unsubscribeTcs?.TrySetResult(true);
            });
        }
    }
}
#endif
