#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_IOS
namespace UniBLE.Platforms.Apple
{
    internal static class AppleNativeConstants
    {
#if UNITY_IOS && !UNITY_EDITOR
        internal const string DllName = "__Internal";
#else
        internal const string DllName = "UniBlePlugin";
#endif
    }
}
#endif
