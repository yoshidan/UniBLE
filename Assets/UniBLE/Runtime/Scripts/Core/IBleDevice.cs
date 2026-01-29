using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniBLE
{
    /// <summary>
    /// Interface for BLE device
    /// </summary>
    public interface IBleDevice
    {
        /// <summary>
        /// Device ID (platform-specific identifier)
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Device name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Connection state
        /// </summary>
        BleConnectionState ConnectionState { get; }

        /// <summary>
        /// Event fired when connection state changes
        /// </summary>
        event Action<BleConnectionState> OnConnectionStateChanged;

        /// <summary>
        /// Connect to the device
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Disconnect from the device
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all GATT services
        /// </summary>
        Task<IReadOnlyList<IBleService>> GetServicesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a service by UUID
        /// </summary>
        Task<IBleService> GetServiceAsync(BleUuid uuid, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// BLE connection state
    /// </summary>
    public enum BleConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting
    }
}
