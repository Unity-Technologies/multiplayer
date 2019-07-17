using System.Globalization;
using Unity.Mathematics;
using UnityEngine;

[ExecuteInEditMode]
public class Transform2dGhostSnapshotValue : MonoBehaviour
{
#if UNITY_EDITOR
    private void OnEnable()
    {
        GhostSnapshotValue.GameSpecificTypes.Add(new GhostSnapshotValue2DRotation());
        GhostSnapshotValue.GameSpecificTypes.Add(new GhostSnapshotValue2DTranslation());
    }

    private void OnDisable()
    {
        for (int i = 0; i < GhostSnapshotValue.GameSpecificTypes.Count; ++i)
        {
            if (GhostSnapshotValue.GameSpecificTypes[i] is GhostSnapshotValue2DRotation ||
                GhostSnapshotValue.GameSpecificTypes[i] is GhostSnapshotValue2DTranslation)
            {
                GhostSnapshotValue.GameSpecificTypes.RemoveAt(i);
                --i;
            }
        }
    }
#endif
}

#if UNITY_EDITOR
class GhostSnapshotValue2DRotation : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(quaternion) && componentName == "Rotation" && fieldName == "Value";
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateIntMember(componentName + fieldName);
    }

    private const string k_SnapshotQuaternionWFieldGetSetTemplate = @"    public quaternion Get$(GHOSTFIELD)()
    {
        var qw = $(GHOSTFIELD) * $(GHOSTDEQUANT);
        return new quaternion(0, 0, math.abs(qw) > 1-1e-9?0:math.sqrt(1-qw*qw), qw);
    }
    public void Set$(GHOSTFIELD)(quaternion q)
    {
        $(GHOSTFIELD) = (int) ((q.value.z >= 0 ? q.value.w : -q.value.w) * $(GHOSTQUANT));
    }
";
    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotQuaternionWFieldGetSetTemplate
            .Replace("$(GHOSTFIELD)", componentName + fieldName)
            .Replace("$(GHOSTQUANT)", quantization.ToString())
            .Replace("$(GHOSTDEQUANT)", (1f/quantization).ToString(CultureInfo.InvariantCulture)+"f");
    }
    public override string GeneratePredict(string componentName, string fieldName)
    {
        return GenerateIntPredict(componentName + fieldName);
    }
    public override string GenerateRead(string componentName, string fieldName)
    {
        return GenerateIntRead(componentName + fieldName);
    }
    public override string GenerateWrite(string componentName, string fieldName)
    {
        return GenerateIntWrite(componentName + fieldName);
    }
    public override string GenerateInterpolate(string componentName, string fieldName)
    {
        return GenerateInterpolateSlerp(componentName+fieldName);
    }
}
class GhostSnapshotValue2DTranslation : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(float3) && componentName == "Translation" && fieldName == "Value";
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateIntMember(componentName + fieldName + "X") +
               GenerateIntMember(componentName + fieldName + "Y");
    }

    private const string k_SnapshotFloat3XYFieldGetSetTemplate = @"    public float3 Get$(GHOSTFIELD)()
    {
        return new float3($(GHOSTFIELD)X, $(GHOSTFIELD)Y, 0) * $(GHOSTDEQUANT);
    }
    public void Set$(GHOSTFIELD)(float3 val)
    {
        $(GHOSTFIELD)X = (int)(val.x * $(GHOSTQUANT));
        $(GHOSTFIELD)Y = (int)(val.y * $(GHOSTQUANT));
    }
";
    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotFloat3XYFieldGetSetTemplate
            .Replace("$(GHOSTFIELD)", componentName + fieldName)
            .Replace("$(GHOSTQUANT)", quantization.ToString())
            .Replace("$(GHOSTDEQUANT)", (1f/quantization).ToString(CultureInfo.InvariantCulture)+"f");
    }
    public override string GeneratePredict(string componentName, string fieldName)
    {
        return GenerateIntPredict(componentName + fieldName + "X") +
               GenerateIntPredict(componentName + fieldName + "Y");
    }
    public override string GenerateRead(string componentName, string fieldName)
    {
        return GenerateIntRead(componentName + fieldName + "X") +
               GenerateIntRead(componentName + fieldName + "Y");
    }
    public override string GenerateWrite(string componentName, string fieldName)
    {
        return GenerateIntWrite(componentName + fieldName + "X") +
               GenerateIntWrite(componentName + fieldName + "Y");
    }
    public override string GenerateInterpolate(string componentName, string fieldName)
    {
        return GenerateInterpolateLerp(componentName+fieldName);
    }
}
#endif
