using System;
using UnityEngine;

namespace UniBLE
{
    /// <summary>
    /// Entry point for the BLE library.
    /// Provides the appropriate BLE adapter based on the platform.
    /// </summary>
    public static class BleManager
    {
        private static IBleAdapter _adapter;
        private static readonly object _lock = new object();
        private static bool _debugLogging;

        /// <summary>
        /// Enable or disable debug logging for the BLE library.
        /// When enabled, detailed logs are output to the console and native log (NSLog on Apple).
        /// </summary>
        public static bool DebugLogging
        {
            get => _debugLogging;
            set => _debugLogging = value;
        }

        /// <summary>
        /// Get the singleton instance of the BLE adapter
        /// </summary>
        public static IBleAdapter Adapter
        {
            get
            {
                if (_adapter == null)
                {
                    lock (_lock)
                    {
                        if (_adapter == null)
                        {
                            _adapter = CreateAdapter();
                        }
                    }
                }
                return _adapter;
            }
        }

        /// <summary>
        /// Create an adapter based on the current platform
        /// </summary>
        private static IBleAdapter CreateAdapter()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new Platforms.Android.AndroidBleAdapter();
#elif UNITY_IOS && !UNITY_EDITOR
            return new Platforms.Apple.AppleBleAdapter();
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return new Platforms.Apple.AppleBleAdapter();
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
            throw new BleException(
                BleErrorCode.NotSupported,
                "BLE is not yet supported on Windows platform");
#else
            throw new BleException(
                BleErrorCode.NotSupported,
                $"BLE is not supported on this platform: {Application.platform}");
#endif
        }

        /// <summary>
        /// Reset the adapter (mainly for testing purposes)
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _adapter = null;
            }
        }
    }
}
