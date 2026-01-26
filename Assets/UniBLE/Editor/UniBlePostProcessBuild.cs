#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

namespace UniBLE.Editor
{
    public static class UniBlePostProcessBuild
    {
        [PostProcessBuild]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string path)
        {
            if (buildTarget != BuildTarget.iOS) return;

            // Add CoreBluetooth.framework
            var projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);

            var targetGuid = proj.GetUnityFrameworkTargetGuid();
            proj.AddFrameworkToProject(targetGuid, "CoreBluetooth.framework", false);

            proj.WriteToFile(projPath);

            // Add Bluetooth usage description to Info.plist
            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            var rootDict = plist.root;
            if (rootDict["NSBluetoothAlwaysUsageDescription"] == null)
            {
                rootDict.SetString("NSBluetoothAlwaysUsageDescription", "This app uses Bluetooth to communicate with BLE devices.");
            }

            plist.WriteToFile(plistPath);
        }
    }
}
#endif
