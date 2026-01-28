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
    /// macOS BLE service implementation
    /// </summary>
    public class AppleBleService : IBleService
    {
        private static readonly Dictionary<string, AppleBleService> _services = new Dictionary<string, AppleBleService>();

#if UNITY_IOS && !UNITY_EDITOR
        private const string DllName = "__Internal";
#else
        private const string DllName = "UniBlePlugin";
#endif
        private readonly Dictionary<BleUuid, AppleBleCharacteristic> _characteristics = new Dictionary<BleUuid, AppleBleCharacteristic>();
        private readonly string _deviceId;
        private TaskCompletionSource<IReadOnlyList<IBleCharacteristic>> _discoverCharacteristicsTcs;

        public BleUuid Uuid { get; }

        #region Native Methods
        [DllImport(DllName)]
        private static extern void UniBle_DiscoverCharacteristics(string deviceId, string serviceUuid, CharacteristicDiscoveryCallback callback);
        #endregion

        #region Callbacks
        private delegate void CharacteristicDiscoveryCallback(string deviceId, string serviceUuid, string characteristicsJson, string error);

        private static CharacteristicDiscoveryCallback _characteristicDiscoveryCallback;
        #endregion

        static AppleBleService()
        {
            _characteristicDiscoveryCallback = OnNativeCharacteristicsDiscovered;
        }

        internal AppleBleService(BleUuid uuid, string deviceId)
        {
            Uuid = uuid;
            _deviceId = deviceId;
            var key = GetKey(deviceId, uuid.ToFullString());
            _services[key] = this;
        }

        private static string GetKey(string deviceId, string serviceUuid)
        {
            return $"{deviceId}:{serviceUuid}";
        }

        internal static void RemoveForDevice(string deviceId)
        {
            var keysToRemove = new List<string>();
            var prefix = deviceId + ":";
            foreach (var key in _services.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    keysToRemove.Add(key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _services.Remove(key);
            }
        }

        public Task<IReadOnlyList<IBleCharacteristic>> GetCharacteristicsAsync(CancellationToken cancellationToken = default)
        {
            _discoverCharacteristicsTcs?.TrySetCanceled();
            _discoverCharacteristicsTcs = new TaskCompletionSource<IReadOnlyList<IBleCharacteristic>>();
            cancellationToken.Register(() => _discoverCharacteristicsTcs.TrySetCanceled());

            MainThreadDispatcher.Enqueue(() =>
            {
                UniBle_DiscoverCharacteristics(_deviceId, Uuid.ToString(), _characteristicDiscoveryCallback);
            });

            return _discoverCharacteristicsTcs.Task;
        }

        public async Task<IBleCharacteristic> GetCharacteristicAsync(BleUuid uuid, CancellationToken cancellationToken = default)
        {
            var characteristics = await GetCharacteristicsAsync(cancellationToken);
            foreach (var characteristic in characteristics)
            {
                if (characteristic.Uuid == uuid)
                {
                    return characteristic;
                }
            }
            throw new BleException(BleErrorCode.CharacteristicNotFound, $"Characteristic not found: {uuid}");
        }

        [MonoPInvokeCallback(typeof(CharacteristicDiscoveryCallback))]
        private static void OnNativeCharacteristicsDiscovered(string deviceId, string serviceUuid, string characteristicsJson, string error)
        {
            MainThreadDispatcher.Enqueue(() =>
            {
                var svcUuid = new BleUuid(serviceUuid);
                var key = GetKey(deviceId, svcUuid.ToFullString());
                if (!_services.TryGetValue(key, out var service)) return;

                if (!string.IsNullOrEmpty(error))
                {
                    service._discoverCharacteristicsTcs?.TrySetException(new BleException(BleErrorCode.CharacteristicNotFound, error));
                    return;
                }

                service._characteristics.Clear();
                var result = new List<IBleCharacteristic>();

                // Parse JSON array: [{"uuid": "...", "properties": 123}, ...]
                if (!string.IsNullOrEmpty(characteristicsJson))
                {
                    var items = ParseCharacteristicsJson(characteristicsJson);
                    foreach (var item in items)
                    {
                        var charUuid = new BleUuid(item.uuid);
                        var characteristic = new AppleBleCharacteristic(charUuid, item.properties, deviceId, svcUuid);
                        service._characteristics[charUuid] = characteristic;
                        result.Add(characteristic);
                    }
                }

                service._discoverCharacteristicsTcs?.TrySetResult(result);
            });
        }

        private static List<(string uuid, BleCharacteristicProperties properties)> ParseCharacteristicsJson(string json)
        {
            var result = new List<(string uuid, BleCharacteristicProperties properties)>();
            if (string.IsNullOrEmpty(json)) return result;

            // Simple JSON parser for [{"uuid": "str", "properties": num}, ...]
            json = json.Trim();
            if (!json.StartsWith("[") || !json.EndsWith("]")) return result;

            json = json.Substring(1, json.Length - 2);

            int depth = 0;
            int start = 0;
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') depth--;
                else if (json[i] == ',' && depth == 0)
                {
                    var obj = json.Substring(start, i - start).Trim();
                    var parsed = ParseCharacteristicObject(obj);
                    if (parsed.HasValue) result.Add(parsed.Value);
                    start = i + 1;
                }
            }

            // Last item
            if (start < json.Length)
            {
                var obj = json.Substring(start).Trim();
                var parsed = ParseCharacteristicObject(obj);
                if (parsed.HasValue) result.Add(parsed.Value);
            }

            return result;
        }

        private static (string uuid, BleCharacteristicProperties properties)? ParseCharacteristicObject(string obj)
        {
            if (string.IsNullOrEmpty(obj)) return null;

            obj = obj.Trim();
            if (!obj.StartsWith("{") || !obj.EndsWith("}")) return null;

            obj = obj.Substring(1, obj.Length - 2);

            string uuid = null;
            int properties = 0;

            var parts = obj.Split(',');
            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length != 2) continue;

                var key = kv[0].Trim().Trim('"');
                var value = kv[1].Trim().Trim('"');

                if (key == "uuid")
                {
                    uuid = value;
                }
                else if (key == "properties")
                {
                    int.TryParse(value, out properties);
                }
            }

            if (string.IsNullOrEmpty(uuid)) return null;
            return (uuid, (BleCharacteristicProperties)properties);
        }
    }
}
#endif
