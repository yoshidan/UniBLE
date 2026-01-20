using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniBLE
{
    /// <summary>
    /// Interface for BLE adapter
    /// </summary>
    public interface IBleAdapter
    {
        /// <summary>
        /// Current state of BLE adapter
        /// </summary>
        BleAdapterState State { get; }

        /// <summary>
        /// Event fired when BLE adapter state changes
        /// </summary>
        event Action<BleAdapterState> OnStateChanged;

        /// <summary>
        /// Check if BLE is available
        /// </summary>
        Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Request BLE permissions (if needed)
        /// </summary>
        Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Start scanning for devices
        /// </summary>
        /// <param name="serviceUuids">Service UUIDs to filter (null for all devices)</param>
        /// <param name="onDeviceDiscovered">Callback when a device is discovered</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task StartScanAsync(
            IEnumerable<string> serviceUuids,
            Action<IBleDevice> onDeviceDiscovered,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stop scanning for devices
        /// </summary>
        Task StopScanAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Connect to a known device
        /// </summary>
        /// <param name="deviceId">Device ID</param>
        Task<IBleDevice> ConnectToKnownDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// BLE adapter state
    /// </summary>
    public enum BleAdapterState
    {
        Unknown,
        Unsupported,
        Unauthorized,
        PoweredOff,
        PoweredOn
    }
}
