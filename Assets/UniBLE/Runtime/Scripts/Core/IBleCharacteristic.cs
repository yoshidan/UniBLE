using System;
using System.Threading;
using System.Threading.Tasks;

namespace UniBLE
{
    /// <summary>
    /// Interface for BLE GATT Characteristic
    /// </summary>
    public interface IBleCharacteristic
    {
        /// <summary>
        /// UUID of the characteristic
        /// </summary>
        string Uuid { get; }

        /// <summary>
        /// Properties of the characteristic
        /// </summary>
        BleCharacteristicProperties Properties { get; }

        /// <summary>
        /// Read the value
        /// </summary>
        Task<byte[]> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Write a value
        /// </summary>
        Task WriteAsync(byte[] data, bool withResponse = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Subscribe to Notify/Indicate
        /// </summary>
        Task SubscribeAsync(Action<byte[]> onNotify, CancellationToken cancellationToken = default);

        /// <summary>
        /// Unsubscribe from Notify/Indicate
        /// </summary>
        Task UnsubscribeAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Characteristic properties
    /// </summary>
    [Flags]
    public enum BleCharacteristicProperties
    {
        None = 0,
        Broadcast = 1 << 0,
        Read = 1 << 1,
        WriteWithoutResponse = 1 << 2,
        Write = 1 << 3,
        Notify = 1 << 4,
        Indicate = 1 << 5,
        AuthenticatedSignedWrites = 1 << 6,
        ExtendedProperties = 1 << 7
    }
}
