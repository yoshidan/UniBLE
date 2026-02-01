using System;

namespace UniBLE
{
    /// <summary>
    /// Represents a Bluetooth UUID. Handles short (16-bit), medium (32-bit),
    /// and full (128-bit) UUID forms. Equality is based on the full 128-bit form
    /// so that new BleUuid("2a19") == new BleUuid("00002a19-0000-1000-8000-00805f9b34fb").
    /// </summary>
    public readonly struct BleUuid : IEquatable<BleUuid>, IComparable<BleUuid>
    {
        private const string BaseUuidSuffix = "-0000-1000-8000-00805f9b34fb";
        private const string BaseUuidPrefix = "0000";

        private readonly string _fullUuid;

        /// <summary>
        /// Construct from any UUID string form: 4-char (16-bit), 8-char (32-bit), or 36-char (128-bit).
        /// </summary>
        public BleUuid(string uuid)
        {
            if (string.IsNullOrEmpty(uuid))
                throw new ArgumentException("UUID cannot be null or empty", nameof(uuid));

            uuid = uuid.Trim().ToLowerInvariant();

            switch (uuid.Length)
            {
                case 4:
                    if (!IsHex(uuid))
                        throw new ArgumentException($"Invalid UUID format: \"{uuid}\"", nameof(uuid));
                    _fullUuid = BaseUuidPrefix + uuid + BaseUuidSuffix;
                    break;
                case 8:
                    if (!IsHex(uuid))
                        throw new ArgumentException($"Invalid UUID format: \"{uuid}\"", nameof(uuid));
                    _fullUuid = uuid + BaseUuidSuffix;
                    break;
                case 36:
                    if (!Guid.TryParseExact(uuid, "D", out _))
                        throw new ArgumentException($"Invalid UUID format: \"{uuid}\"", nameof(uuid));
                    _fullUuid = uuid;
                    break;
                default:
                    throw new ArgumentException(
                        $"Invalid UUID length {uuid.Length}. Expected 4 (16-bit), 8 (32-bit), or 36 (128-bit): \"{uuid}\"",
                        nameof(uuid));
            }
        }

        /// <summary>
        /// Returns the shortest form for standard Bluetooth SIG UUIDs (e.g. "2a19"),
        /// or the full 128-bit form for custom UUIDs.
        /// </summary>
        public override string ToString()
        {
            var full = ToFullString();
            if (full.Length == 36 &&
                full.StartsWith(BaseUuidPrefix, StringComparison.Ordinal) &&
                full.Substring(8) == BaseUuidSuffix)
            {
                return full.Substring(4, 4);
            }
            return full;
        }

        /// <summary>
        /// Always returns the full 128-bit UUID string (e.g. "00002a19-0000-1000-8000-00805f9b34fb").
        /// Use this when passing UUIDs to native code.
        /// </summary>
        public string ToFullString()
        {
            if (_fullUuid == null)
                throw new InvalidOperationException("BleUuid is uninitialized");
            return _fullUuid;
        }

        public bool Equals(BleUuid other)
        {
            return string.Equals(ToFullString(), other.ToFullString(), StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is BleUuid other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ToFullString().GetHashCode();
        }

        public int CompareTo(BleUuid other)
        {
            return string.Compare(ToFullString(), other.ToFullString(), StringComparison.Ordinal);
        }

        public static bool operator ==(BleUuid left, BleUuid right) => left.Equals(right);
        public static bool operator !=(BleUuid left, BleUuid right) => !left.Equals(right);

        public static implicit operator BleUuid(string uuid) => new BleUuid(uuid);

        private static bool IsHex(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isDigit = c >= '0' && c <= '9';
                bool isLowerHex = c >= 'a' && c <= 'f';
                if (!isDigit && !isLowerHex)
                    return false;
            }
            return true;
        }
    }
}
