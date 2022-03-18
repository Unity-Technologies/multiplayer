using UnityEngine;
using UnityEngine.EventSystems;

public class TouchInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public enum KeyCode
    {
        Left = 0,
        Right,
        Up,
        Down,
        Space,
        NumKeys
    }
    private static bool[] ActiveKeys = new bool[(int)KeyCode.NumKeys];
    public KeyCode Key;

    public static bool GetKey(KeyCode code)
    {
        return ActiveKeys[(int)code];
    }

    void Start()
    {
        #if !UNITY_ANDROID && !UNITY_IOS
        gameObject.SetActive(false);
        #endif
        ActiveKeys[(int)Key] = false;
    }
    public void OnPointerDown(PointerEventData eventData)
    {
        ActiveKeys[(int)Key] = true;
    }
    public void OnPointerUp(PointerEventData eventData)
    {
        ActiveKeys[(int)Key] = false;
    }
}
