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

            var projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);

            var targetGuid = proj.GetUnityFrameworkTargetGuid();

            proj.AddFrameworkToProject(targetGuid, "CoreBluetooth.framework", false);

            proj.WriteToFile(projPath);
        }
    }
}
#endif
