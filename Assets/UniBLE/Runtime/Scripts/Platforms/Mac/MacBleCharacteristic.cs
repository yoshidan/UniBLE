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
    /// macOS BLE characteristic implementation
    /// </summary>
    public class MacBleCharacteristic : IBleCharacteristic
    {
        private static readonly Dictionary<string, MacBleCharacteristic> _characteristics = new Dictionary<string, MacBleCharacteristic>();
        private readonly string _deviceId;
        private readonly string _serviceUuid;
        private TaskCompletionSource<byte[]> _readTcs;
        private TaskCompletionSource<bool> _writeTcs;
        private TaskCompletionSource<bool> _subscribeTcs;
        private TaskCompletionSource<bool> _unsubscribeTcs;

        public string Uuid { get; }
        public BleCharacteristicProperties Properties { get; }
        private Action<byte[]> _onNotify;

        #region Native Methods
        [DllImport("UniBlePlugin")]
        private static extern void UniBle_ReadCharacteristic(string deviceId, string serviceUuid, string characteristicUuid, ReadCallback callback);

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_WriteCharacteristic(string deviceId, string serviceUuid, string characteristicUuid, byte[] data, int dataLength, bool withResponse, WriteCallback callback);

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_Subscribe(string deviceId, string serviceUuid, string characteristicUuid, NotifyCallback notifyCallback, SubscribeCallback resultCallback);

        [DllImport("UniBlePlugin")]
        private static extern void UniBle_Unsubscribe(string deviceId, string serviceUuid, string characteristicUuid, SubscribeCallback resultCallback);
        #endregion

        #region Callbacks
        private delegate void ReadCallback(string deviceId, string characteristicUuid, IntPtr data, int dataLength, string error);
        private delegate void WriteCallback(string deviceId, string characteristicUuid, string error);
        private delegate void NotifyCallback(string deviceId, string characteristicUuid, IntPtr data, int dataLength);
        private delegate void SubscribeCallback(string deviceId, string characteristicUuid, string error);

        private static ReadCallback _readCallback;
        private static WriteCallback _writeCallback;
        private static NotifyCallback _notifyCallback;
        private static SubscribeCallback _subscribeCallback;
        private static SubscribeCallback _unsubscribeCallback;
        #endregion

        static MacBleCharacteristic()
        {
            _readCallback = OnNativeReadResult;
            _writeCallback = OnNativeWriteResult;
            _notifyCallback = OnNativeNotify;
            _subscribeCallback = OnNativeSubscribeResult;
            _unsubscribeCallback = OnNativeUnsubscribeResult;
        }

        internal MacBleCharacteristic(string uuid, BleCharacteristicProperties properties, string deviceId, string serviceUuid)
        {
            Uuid = uuid;
            Properties = properties;
            _deviceId = deviceId;
            _serviceUuid = serviceUuid;
            var key = GetKey(deviceId, serviceUuid, uuid);
            _characteristics[key] = this;
        }

        private static string GetKey(string deviceId, string serviceUuid, string characteristicUuid)
        {
            return $"{deviceId}:{serviceUuid}:{characteristicUuid}";
        }

        public Task<byte[]> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!Properties.HasFlag(BleCharacteristicProperties.Read))
            {
                throw new BleException(BleErrorCode.NotSupported, "Characteristic does not support reading");
            }

            _readTcs = new TaskCompletionSource<byte[]>();
            cancellationToken.Register(() => _readTcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_ReadCharacteristic(_deviceId, _serviceUuid, Uuid, _readCallback);
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

            _writeTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _writeTcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_WriteCharacteristic(_deviceId, _serviceUuid, Uuid, data, data.Length, withResponse, _writeCallback);
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
            _subscribeTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _subscribeTcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_Subscribe(_deviceId, _serviceUuid, Uuid, _notifyCallback, _subscribeCallback);
            });

            return _subscribeTcs.Task;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            _onNotify = null;
            _unsubscribeTcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => _unsubscribeTcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_Unsubscribe(_deviceId, _serviceUuid, Uuid, _unsubscribeCallback);
            });

            return _unsubscribeTcs.Task;
        }

        [MonoPInvokeCallback(typeof(ReadCallback))]
        private static void OnNativeReadResult(string deviceId, string characteristicUuid, IntPtr data, int dataLength, string error)
        {
            byte[] dataArray = null;
            if (data != IntPtr.Zero && dataLength > 0)
            {
                dataArray = new byte[dataLength];
                Marshal.Copy(data, dataArray, 0, dataLength);
            }

            MainThreadDispatcher.Enqueue(() =>
            {
                // Find characteristic by iterating (we don't have serviceUuid in callback)
                MacBleCharacteristic characteristic = null;
                foreach (var kvp in _characteristics)
                {
                    if (kvp.Key.StartsWith(deviceId + ":") && kvp.Key.EndsWith(":" + characteristicUuid))
                    {
                        characteristic = kvp.Value;
                        break;
                    }
                }

                if (characteristic == null) return;

                if (!string.IsNullOrEmpty(error))
                {
                    characteristic._readTcs?.TrySetException(new BleException(BleErrorCode.ReadFailed, error));
                    return;
                }

                characteristic._readTcs?.TrySetResult(dataArray ?? new byte[0]);
            });
        }

        [MonoPInvokeCallback(typeof(WriteCallback))]
        private static void OnNativeWriteResult(string deviceId, string characteristicUuid, string error)
        {
            MainThreadDispatcher.Enqueue(() =>
            {
                MacBleCharacteristic characteristic = null;
                foreach (var kvp in _characteristics)
                {
                    if (kvp.Key.StartsWith(deviceId + ":") && kvp.Key.EndsWith(":" + characteristicUuid))
                    {
                        characteristic = kvp.Value;
                        break;
                    }
                }

                if (characteristic == null) return;

                if (!string.IsNullOrEmpty(error))
                {
                    characteristic._writeTcs?.TrySetException(new BleException(BleErrorCode.WriteFailed, error));
                    return;
                }

                characteristic._writeTcs?.TrySetResult(true);
            });
        }

        [MonoPInvokeCallback(typeof(NotifyCallback))]
        private static void OnNativeNotify(string deviceId, string characteristicUuid, IntPtr data, int dataLength)
        {
            byte[] dataArray = null;
            if (data != IntPtr.Zero && dataLength > 0)
            {
                dataArray = new byte[dataLength];
                Marshal.Copy(data, dataArray, 0, dataLength);
            }

            MainThreadDispatcher.Enqueue(() =>
            {
                MacBleCharacteristic characteristic = null;
                foreach (var kvp in _characteristics)
                {
                    if (kvp.Key.StartsWith(deviceId + ":") && kvp.Key.EndsWith(":" + characteristicUuid))
                    {
                        characteristic = kvp.Value;
                        break;
                    }
                }

                if (characteristic == null) return;
                characteristic._onNotify?.Invoke(dataArray ?? new byte[0]);
            });
        }

        [MonoPInvokeCallback(typeof(SubscribeCallback))]
        private static void OnNativeSubscribeResult(string deviceId, string characteristicUuid, string error)
        {
            MainThreadDispatcher.Enqueue(() =>
            {
                MacBleCharacteristic characteristic = null;
                foreach (var kvp in _characteristics)
                {
                    if (kvp.Key.StartsWith(deviceId + ":") && kvp.Key.EndsWith(":" + characteristicUuid))
                    {
                        characteristic = kvp.Value;
                        break;
                    }
                }

                if (characteristic == null) return;

                if (!string.IsNullOrEmpty(error))
                {
                    characteristic._subscribeTcs?.TrySetException(new BleException(BleErrorCode.NotificationFailed, error));
                    return;
                }

                characteristic._subscribeTcs?.TrySetResult(true);
            });
        }

        [MonoPInvokeCallback(typeof(SubscribeCallback))]
        private static void OnNativeUnsubscribeResult(string deviceId, string characteristicUuid, string error)
        {
            MainThreadDispatcher.Enqueue(() =>
            {
                MacBleCharacteristic characteristic = null;
                foreach (var kvp in _characteristics)
                {
                    if (kvp.Key.StartsWith(deviceId + ":") && kvp.Key.EndsWith(":" + characteristicUuid))
                    {
                        characteristic = kvp.Value;
                        break;
                    }
                }

                if (characteristic == null) return;

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
