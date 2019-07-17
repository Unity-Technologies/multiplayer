using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class DedicatedServerConfig : MonoBehaviour
{
    static DateTime s_LastActivity = DateTime.UtcNow;

    [Tooltip("Time (in seconds) until the server shuts down due to inactivity. 0 = disabled. Can be set at runtime with the -timeout argument.")]
    public ushort InactivityTimeoutSeconds = 600;

    [Tooltip("Force target FPS.  Used to limit CPU usage on headless servers. 0 = default fps. Can be set at runtime with the -fps argument.")]
    public ushort TargetFramerate = 60;

    public static void UpdateLastActivity()
    {
        s_LastActivity = DateTime.UtcNow;
    }

    void Start()
    {
        // Handle setting FPS via startup argument
        if (CommandLine.TryGetCommandLineArgValue("-fps", out ushort targetFps))
            TargetFramerate = targetFps;
        else if (CommandLine.HasArgument("-fps"))
            Debug.LogWarning($"Unable to set target fps: -fps must be a number between 1 and {ushort.MaxValue}");

        // Handle setting FPS if set on a Unity object
        if (TargetFramerate > 0)
        {
            Application.targetFrameRate = TargetFramerate;
            Debug.Log($"Setting application target framerate to {TargetFramerate}");

            // If requested FPS is different from current screen resolution, disable VSync
            if (targetFps != Screen.currentResolution.refreshRate)
                QualitySettings.vSyncCount = 0;
        }

        // Handle setting inactivity timeout via startup argument
        if (CommandLine.TryGetCommandLineArgValue("-timeout", out ushort timeoutSeconds))
        {
            Debug.Log($"Setting application inactivity timeout to {timeoutSeconds}");
            InactivityTimeoutSeconds = timeoutSeconds;
        }

        if (InactivityTimeoutSeconds == 0)
            Debug.Log($"Inactivity timeout set to 0; disabling inactivity timeout checks.");
    }

    void FixedUpdate()
    {
        if (InactivityTimeoutSeconds > 0 && (DateTime.UtcNow - s_LastActivity).TotalSeconds > InactivityTimeoutSeconds)
        {
            // Shut it down
            Debug.Log("Shutting down server due to inactivity timeout (" + InactivityTimeoutSeconds + " seconds)");

#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
