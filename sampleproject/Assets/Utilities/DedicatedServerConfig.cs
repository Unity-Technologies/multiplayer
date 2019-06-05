using System;
using UnityEngine;

public class DedicatedServerConfig : MonoBehaviour
{
    public int InactivityTimeoutSeconds = 0;
    private static DateTime LastActivity = DateTime.UtcNow;
    public int TargetFramerate = -1;

    public static void UpdateLastActivity()
    {
        LastActivity = DateTime.UtcNow;
    }

    void Start()
    {
        // Mainly used to limit CPU usage on headless servers.
        if (TargetFramerate != -1)
            Application.targetFrameRate = TargetFramerate;

    }

    void FixedUpdate()
    {
        if (InactivityTimeoutSeconds > 0 && (DateTime.UtcNow - LastActivity).TotalSeconds > InactivityTimeoutSeconds)
        {
            // Shut it down
            Debug.Log("Shutting down server due to inactivity timeout (" + InactivityTimeoutSeconds + " seconds)");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
