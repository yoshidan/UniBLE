using System;

namespace UniBLE
{
    /// <summary>
    /// Exception for BLE operations
    /// </summary>
    public class BleException : Exception
    {
        /// <summary>
        /// Error code
        /// </summary>
        public BleErrorCode ErrorCode { get; }

        public BleException(BleErrorCode errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        public BleException(BleErrorCode errorCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// BLE error codes
    /// </summary>
    public enum BleErrorCode
    {
        Unknown,
        NotSupported,
        NotAuthorized,
        PoweredOff,
        NotConnected,
        AlreadyConnected,
        ConnectionFailed,
        ConnectionLost,
        ServiceNotFound,
        CharacteristicNotFound,
        ReadFailed,
        WriteFailed,
        NotificationFailed,
        ScanFailed,
        Timeout
    }
}
