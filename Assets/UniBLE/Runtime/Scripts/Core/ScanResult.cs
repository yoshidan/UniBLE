using System.Collections.Generic;

namespace UniBLE
{
    /// <summary>
    /// Additional information from scan result
    /// </summary>
    public class ScanResult
    {
        /// <summary>
        /// Discovered device
        /// </summary>
        public IBleDevice Device { get; }

        /// <summary>
        /// RSSI (Received Signal Strength Indicator)
        /// </summary>
        public int Rssi { get; }

        /// <summary>
        /// Advertisement data
        /// </summary>
        public IReadOnlyDictionary<string, byte[]> AdvertisementData { get; }

        public ScanResult(IBleDevice device, int rssi, IReadOnlyDictionary<string, byte[]> advertisementData)
        {
            Device = device;
            Rssi = rssi;
            AdvertisementData = advertisementData;
        }
    }
}
