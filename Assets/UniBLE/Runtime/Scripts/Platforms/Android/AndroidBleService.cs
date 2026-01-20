#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UniBLE.Platforms.Android
{
    /// <summary>
    /// Android BLE service implementation
    /// </summary>
    public class AndroidBleService : IBleService
    {
        private readonly AndroidJavaObject _plugin;
        private readonly string _deviceId;
        private readonly Dictionary<string, AndroidBleCharacteristic> _characteristics = new Dictionary<string, AndroidBleCharacteristic>();

        public string Uuid { get; }

        internal AndroidBleService(string uuid, AndroidJavaObject plugin, string deviceId)
        {
            Uuid = uuid;
            _plugin = plugin;
            _deviceId = deviceId;
        }

        public Task<IReadOnlyList<IBleCharacteristic>> GetCharacteristicsAsync(CancellationToken cancellationToken = default)
        {
            _characteristics.Clear();
            var result = new List<IBleCharacteristic>();

            // Call Java plugin to get characteristics array
            var characteristics = _plugin.Call<AndroidJavaObject[]>("getCharacteristics", _deviceId, Uuid);
            if (characteristics != null)
            {
                foreach (var characteristic in characteristics)
                {
                    var uuid = characteristic.Call<AndroidJavaObject>("getUuid").Call<string>("toString");
                    var properties = characteristic.Call<int>("getProperties");
                    var bleCharacteristic = new AndroidBleCharacteristic(uuid, properties, _plugin, _deviceId, Uuid);
                    _characteristics[uuid] = bleCharacteristic;
                    result.Add(bleCharacteristic);
                }
            }

            return Task.FromResult<IReadOnlyList<IBleCharacteristic>>(result);
        }

        public async Task<IBleCharacteristic> GetCharacteristicAsync(string uuid, CancellationToken cancellationToken = default)
        {
            var characteristics = await GetCharacteristicsAsync(cancellationToken);
            foreach (var characteristic in characteristics)
            {
                if (characteristic.Uuid.Equals(uuid, StringComparison.OrdinalIgnoreCase))
                {
                    return characteristic;
                }
            }
            throw new BleException(BleErrorCode.CharacteristicNotFound, $"Characteristic not found: {uuid}");
        }
    }
}
#endif
