using UnityEngine;

public class LagUI : MonoBehaviour
{
    public static bool EnableLagCompensation = true;
    public static uint ClientTick;
    public static uint ServerTick;
    public static bool ClientHit;
    public static bool ServerHit;
    private void OnGUI()
    {
        EnableLagCompensation = GUI.Toggle(new Rect(10, 10, 200, 20), EnableLagCompensation, "Enable lag compensation");
        GUI.Label(new Rect(10, 30, 200, 20), $"Client: {(ClientHit?"hit":"miss")} @ tick {ClientTick}");
        GUI.Label(new Rect(10, 50, 200, 20), $"Server: {(ServerHit?"hit":"miss")} @ tick {ServerTick}");
    }
}
