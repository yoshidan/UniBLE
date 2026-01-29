#if UNITY_ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UniBLE.Platforms.Android
{
    /// <summary>
    /// Android BLE characteristic implementation
    /// </summary>
    public class AndroidBleCharacteristic : IBleCharacteristic
    {
        private readonly AndroidJavaObject _plugin;
        private readonly string _deviceId;
        private readonly BleUuid _serviceUuid;
        private Action<byte[]> _notifyCallback;
        private TaskCompletionSource<byte[]> _readTcs;
        private TaskCompletionSource<bool> _writeTcs;

        public BleUuid Uuid { get; }
        public BleCharacteristicProperties Properties { get; }

        internal AndroidBleCharacteristic(BleUuid uuid, int androidProperties, AndroidJavaObject plugin, string deviceId, BleUuid serviceUuid)
        {
            Uuid = uuid;
            Properties = ConvertProperties(androidProperties);
            _plugin = plugin;
            _deviceId = deviceId;
            _serviceUuid = serviceUuid;
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

            MainThreadDispatcher.Enqueue(() =>
            {
                _plugin.Call("readCharacteristic", _deviceId, _serviceUuid.ToFullString(), Uuid.ToFullString(), new ReadCallback(this));
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

            MainThreadDispatcher.Enqueue(() =>
            {
                _plugin.Call("writeCharacteristic", _deviceId, _serviceUuid.ToFullString(), Uuid.ToFullString(), data, withResponse, new WriteCallback(this));
            });

            return _writeTcs.Task;
        }

        public Task SubscribeAsync(Action<byte[]> onNotify, CancellationToken cancellationToken = default)
        {
            if (!Properties.HasFlag(BleCharacteristicProperties.Notify) && !Properties.HasFlag(BleCharacteristicProperties.Indicate))
            {
                throw new BleException(BleErrorCode.NotSupported, "Characteristic does not support notifications");
            }

            _notifyCallback = onNotify;

            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => tcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                _plugin.Call("subscribeCharacteristic", _deviceId, _serviceUuid.ToFullString(), Uuid.ToFullString(), new NotifyCallback(this), new SubscribeResultCallback(tcs));
            });

            return tcs.Task;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            _notifyCallback = null;

            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => tcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                _plugin.Call("unsubscribeCharacteristic", _deviceId, _serviceUuid.ToFullString(), Uuid.ToFullString(), new SubscribeResultCallback(tcs));
            });

            return tcs.Task;
        }

        internal void OnReadSuccess(byte[] data)
        {
            _readTcs?.TrySetResult(data);
        }

        internal void OnReadFailed(string error)
        {
            _readTcs?.TrySetException(new BleException(BleErrorCode.ReadFailed, error));
        }

        internal void OnWriteSuccess()
        {
            _writeTcs?.TrySetResult(true);
        }

        internal void OnWriteFailed(string error)
        {
            _writeTcs?.TrySetException(new BleException(BleErrorCode.WriteFailed, error));
        }

        internal void OnNotify(byte[] data)
        {
            _notifyCallback?.Invoke(data);
        }

        private static BleCharacteristicProperties ConvertProperties(int androidProperties)
        {
            var properties = BleCharacteristicProperties.None;

            if ((androidProperties & 0x01) != 0) properties |= BleCharacteristicProperties.Broadcast;
            if ((androidProperties & 0x02) != 0) properties |= BleCharacteristicProperties.Read;
            if ((androidProperties & 0x04) != 0) properties |= BleCharacteristicProperties.WriteWithoutResponse;
            if ((androidProperties & 0x08) != 0) properties |= BleCharacteristicProperties.Write;
            if ((androidProperties & 0x10) != 0) properties |= BleCharacteristicProperties.Notify;
            if ((androidProperties & 0x20) != 0) properties |= BleCharacteristicProperties.Indicate;
            if ((androidProperties & 0x40) != 0) properties |= BleCharacteristicProperties.AuthenticatedSignedWrites;
            if ((androidProperties & 0x80) != 0) properties |= BleCharacteristicProperties.ExtendedProperties;

            return properties;
        }

        /// <summary>
        /// Callback class for read operations
        /// </summary>
        private class ReadCallback : AndroidJavaProxy
        {
            private readonly AndroidBleCharacteristic _characteristic;

            public ReadCallback(AndroidBleCharacteristic characteristic) : base("com.unible.ReadCallback")
            {
                _characteristic = characteristic;
            }

            // Called from Java
            public void onSuccess(byte[] data)
            {
                MainThreadDispatcher.Enqueue(() => _characteristic.OnReadSuccess(data));
            }

            // Called from Java
            public void onError(string error)
            {
                MainThreadDispatcher.Enqueue(() => _characteristic.OnReadFailed(error));
            }
        }

        /// <summary>
        /// Callback class for write operations
        /// </summary>
        private class WriteCallback : AndroidJavaProxy
        {
            private readonly AndroidBleCharacteristic _characteristic;

            public WriteCallback(AndroidBleCharacteristic characteristic) : base("com.unible.WriteCallback")
            {
                _characteristic = characteristic;
            }

            // Called from Java
            public void onSuccess()
            {
                MainThreadDispatcher.Enqueue(() => _characteristic.OnWriteSuccess());
            }

            // Called from Java
            public void onError(string error)
            {
                MainThreadDispatcher.Enqueue(() => _characteristic.OnWriteFailed(error));
            }
        }

        /// <summary>
        /// Callback class for notify events
        /// </summary>
        private class NotifyCallback : AndroidJavaProxy
        {
            private readonly AndroidBleCharacteristic _characteristic;

            public NotifyCallback(AndroidBleCharacteristic characteristic) : base("com.unible.NotifyCallback")
            {
                _characteristic = characteristic;
            }

            // Called from Java on the Android main thread (via mainHandler.post).
            // Invoked directly to ensure delivery even when Unity is paused (background).
            // Note: The callback may fire on a non-Unity thread. If the user needs Unity
            // API access, they should use MainThreadDispatcher.Enqueue() in their handler.
            public void onNotify(byte[] data)
            {
                _characteristic.OnNotify(data);
            }
        }

        /// <summary>
        /// Callback class for subscribe/unsubscribe result
        /// </summary>
        private class SubscribeResultCallback : AndroidJavaProxy
        {
            private readonly TaskCompletionSource<bool> _tcs;

            public SubscribeResultCallback(TaskCompletionSource<bool> tcs) : base("com.unible.SubscribeResultCallback")
            {
                _tcs = tcs;
            }

            // Called from Java
            public void onSuccess()
            {
                MainThreadDispatcher.Enqueue(() => _tcs.TrySetResult(true));
            }

            // Called from Java
            public void onError(string error)
            {
                MainThreadDispatcher.Enqueue(() => _tcs.TrySetException(new BleException(BleErrorCode.NotificationFailed, error)));
            }
        }
    }
}
#endif
