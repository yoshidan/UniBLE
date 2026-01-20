#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniBLE.Platforms.Windows
{
    /// <summary>
    /// Windows用BLEアダプタ実装
    /// </summary>
    public class WindowsBleAdapter : IBleAdapter
    {
        public BleAdapterState State => throw new NotImplementedException();

        public event Action<BleAdapterState> OnStateChanged;

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task StartScanAsync(
            IEnumerable<string> serviceUuids,
            Action<IBleDevice> onDeviceDiscovered,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task StopScanAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IBleDevice> ConnectToKnownDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
#endif
