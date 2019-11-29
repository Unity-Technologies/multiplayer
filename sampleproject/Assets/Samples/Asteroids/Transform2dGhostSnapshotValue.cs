#if UNITY_EDITOR
using System;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

[ExecuteInEditMode]
public class Transform2dGhostSnapshotValue : MonoBehaviour
{
    private GhostSnapshotValue2DRotation rotation;
    private GhostSnapshotValue2DTranslation translation;
    void OnEnable()
    {
        rotation = new GhostSnapshotValue2DRotation();
        translation = new GhostSnapshotValue2DTranslation();
        GhostSnapshotValue.GameSpecificTypes.Add(rotation);
        GhostSnapshotValue.GameSpecificTypes.Add(translation);
    }

    void OnDisable()
    {
        GhostSnapshotValue.GameSpecificTypes.Remove(rotation);
        GhostSnapshotValue.GameSpecificTypes.Remove(translation);
    }
}
class GhostSnapshotValue2DRotation : GhostSnapshotValue
{
    public override bool SupportsQuantization => true;

    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(quaternion) && componentName == "Unity.Transforms.Rotation" && fieldName == "Value";
    }
    public override string GetTemplatePath(int quantization)
    {
        if (quantization < 1)
            throw new InvalidOperationException("Value type does not support unquantized");
        return "Assets/Samples/Asteroids/Editor/GhostSnapshotValue2DRotation.txt";
    }
}
class GhostSnapshotValue2DTranslation : GhostSnapshotValue
{
    public override bool SupportsQuantization => true;

    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(float3) && componentName == "Unity.Transforms.Translation" && fieldName == "Value";
    }
    public override string GetTemplatePath(int quantization)
    {
        if (quantization < 1)
            throw new InvalidOperationException("Value type does not support unquantized");
        return "Assets/Samples/Asteroids/Editor/GhostSnapshotValue2DTranslation.txt";
    }
}
#endif
