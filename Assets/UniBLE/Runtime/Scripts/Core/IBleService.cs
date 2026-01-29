using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniBLE
{
    /// <summary>
    /// Interface for BLE GATT Service
    /// </summary>
    public interface IBleService
    {
        /// <summary>
        /// UUID of the service
        /// </summary>
        BleUuid Uuid { get; }

        /// <summary>
        /// Get all characteristics belonging to this service
        /// </summary>
        Task<IReadOnlyList<IBleCharacteristic>> GetCharacteristicsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a characteristic by UUID
        /// </summary>
        Task<IBleCharacteristic> GetCharacteristicAsync(BleUuid uuid, CancellationToken cancellationToken = default);
    }
}
