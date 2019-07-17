using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class BuildPostprocessor
{
    [UnityEditor.Callbacks.PostProcessBuildAttribute(1)]
    public static void OnPostprocessBuild(UnityEditor.BuildTarget target, string pathToBuiltProject)
    {
        var serverDll = "Asteroids.Server.dll";
        var clientDll = "Asteroids.Client.dll";

        var defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
        var binaryName = pathToBuiltProject.Substring(pathToBuiltProject.LastIndexOf('/'));

        string dllPath = "";

        if (target == BuildTarget.StandaloneOSX)
            dllPath = pathToBuiltProject + "/Contents/Resources/Data/Managed/";
        else if (target == BuildTarget.StandaloneWindows)
            dllPath = pathToBuiltProject.Replace(".exe", "_Data") + "/Managed/";
        else
            throw new Exception("Fill in platform path structure...");

        // Server takes precedence, so you'll always get a server build if Server Build (=enableHeadlessMode) is checked
        if (EditorUserBuildSettings.enableHeadlessMode)
            dllPath += clientDll;
        else if (defines.Contains("UNITY_CLIENT"))
            dllPath += serverDll;


        if (File.Exists(dllPath))
            File.Delete(dllPath);
        else
            Debug.Log("Did not find " + dllPath);
    }
}
