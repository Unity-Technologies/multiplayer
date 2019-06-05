using System.Globalization;
using Unity.Mathematics;

public abstract class GhostSnapshotValue
{
    public abstract bool CanProcess(System.Type type, string componentName, string fieldName);
    public abstract string GenerateMembers(string componentName, string fieldName);
    public abstract string GenerateGetSet(string componentName, string fieldName, int quantization);
    public abstract string GeneratePredict(string componentName, string fieldName);
    public abstract string GenerateRead(string componentName, string fieldName);
    public abstract string GenerateWrite(string componentName, string fieldName);
    public abstract string GenerateInterpolate(string componentName, string fieldName);

    private const string k_SnapshotIntFieldTemplate = @"    int $(GHOSTFIELD);
";
    private const string k_SnapshotUintFieldTemplate = @"    uint $(GHOSTFIELD);
";
    private const string k_SnapshotPredictIntTemplate =
        @"        $(GHOSTFIELD) = predictor.PredictInt($(GHOSTFIELD), baseline1.$(GHOSTFIELD), baseline2.$(GHOSTFIELD));
";
    private const string k_SnapshotPredictUintTemplate =
        @"        $(GHOSTFIELD) = (uint)predictor.PredictInt((int)$(GHOSTFIELD), (int)baseline1.$(GHOSTFIELD), (int)baseline2.$(GHOSTFIELD));
";
    private const string k_SnapshotWriteIntTemplate = @"        writer.WritePackedIntDelta($(GHOSTFIELD), baseline.$(GHOSTFIELD), compressionModel);
";
    private const string k_SnapshotWriteUintTemplate = @"        writer.WritePackedUIntDelta($(GHOSTFIELD), baseline.$(GHOSTFIELD), compressionModel);
";
    private const string k_SnapshotReadIntTemplate = @"        $(GHOSTFIELD) = reader.ReadPackedIntDelta(ref ctx, baseline.$(GHOSTFIELD), compressionModel);
";
    private const string k_SnapshotReadUintTemplate = @"        $(GHOSTFIELD) = reader.ReadPackedUIntDelta(ref ctx, baseline.$(GHOSTFIELD), compressionModel);
";
    protected string GenerateIntMember(string memberName)
    {
        return k_SnapshotIntFieldTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    protected string GenerateUIntMember(string memberName)
    {
        return k_SnapshotUintFieldTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    protected string GenerateIntPredict(string memberName)
    {
        return k_SnapshotPredictIntTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    protected string GenerateUIntPredict(string memberName)
    {
        return k_SnapshotPredictUintTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    protected string GenerateIntRead(string memberName)
    {
        return k_SnapshotReadIntTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    protected string GenerateUIntRead(string memberName)
    {
        return k_SnapshotReadUintTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    protected string GenerateIntWrite(string memberName)
    {
        return k_SnapshotWriteIntTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    protected string GenerateUIntWrite(string memberName)
    {
        return k_SnapshotWriteUintTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    private const string k_SnapshotInterpolateLerpTemplate =
        @"        Set$(GHOSTFIELD)(math.lerp(Get$(GHOSTFIELD)(), target.Get$(GHOSTFIELD)(), factor));
";
    private const string k_SnapshotInterpolateSlerpTemplate =
        @"        Set$(GHOSTFIELD)(math.slerp(Get$(GHOSTFIELD)(), target.Get$(GHOSTFIELD)(), factor));
";

    protected string GenerateInterpolateLerp(string memberName)
    {
        return k_SnapshotInterpolateLerpTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
    protected string GenerateInterpolateSlerp(string memberName)
    {
        return k_SnapshotInterpolateSlerpTemplate.Replace("$(GHOSTFIELD)", memberName);
    }
}

class GhostSnapshotValueQuaternion : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(quaternion);
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateIntMember(componentName + fieldName + "X") +
               GenerateIntMember(componentName + fieldName + "Y") +
               GenerateIntMember(componentName + fieldName + "Z") +
               GenerateIntMember(componentName + fieldName + "W");
    }

    private const string k_SnapshotQuaternionFieldGetSetTemplate = @"    public quaternion Get$(GHOSTFIELD)()
    {
        return new quaternion($(GHOSTFIELD)X, $(GHOSTFIELD)Y, $(GHOSTFIELD)Z, $(GHOSTFIELD)W) * $(GHOSTDEQUANT);
    }
    public void Set$(GHOSTFIELD)(quaternion q)
    {
        $(GHOSTFIELD)X = (int)(q.value.x * $(GHOSTQUANT));
        $(GHOSTFIELD)Y = (int)(q.value.y * $(GHOSTQUANT));
        $(GHOSTFIELD)Z = (int)(q.value.z * $(GHOSTQUANT));
        $(GHOSTFIELD)W = (int)(q.value.w * $(GHOSTQUANT));
    }
";
    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotQuaternionFieldGetSetTemplate
            .Replace("$(GHOSTFIELD)", componentName + fieldName)
            .Replace("$(GHOSTQUANT)", quantization.ToString())
            .Replace("$(GHOSTDEQUANT)", (1f/quantization).ToString(CultureInfo.InvariantCulture)+"f");
    }
    public override string GeneratePredict(string componentName, string fieldName)
    {
        return GenerateIntPredict(componentName + fieldName + "X") +
               GenerateIntPredict(componentName + fieldName + "Y") +
               GenerateIntPredict(componentName + fieldName + "Z") +
               GenerateIntPredict(componentName + fieldName + "W");
    }
    public override string GenerateRead(string componentName, string fieldName)
    {
        return GenerateIntRead(componentName + fieldName + "X") +
               GenerateIntRead(componentName + fieldName + "Y") +
               GenerateIntRead(componentName + fieldName + "Z") +
               GenerateIntRead(componentName + fieldName + "W");
    }
    public override string GenerateWrite(string componentName, string fieldName)
    {
        return GenerateIntWrite(componentName + fieldName + "X") +
               GenerateIntWrite(componentName + fieldName + "Y") +
               GenerateIntWrite(componentName + fieldName + "Z") +
               GenerateIntWrite(componentName + fieldName + "W");
    }
    public override string GenerateInterpolate(string componentName, string fieldName)
    {
        return GenerateInterpolateSlerp(componentName + fieldName);
    }
}
class GhostSnapshotValueFloat : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(float);
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateIntMember(componentName + fieldName);
    }
    private const string k_SnapshotGetSetTemplate = @"    public float Get$(GHOSTFIELD)()
    {
        return (float)$(GHOSTFIELD) * $(GHOSTDEQUANT);
    }
    public void Set$(GHOSTFIELD)(float val)
    {
        $(GHOSTFIELD) = (int)(val * $(GHOSTQUANT));
    }
";

    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotGetSetTemplate
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
        return GenerateInterpolateLerp(componentName + fieldName);
    }
}
class GhostSnapshotValueFloat2 : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(float2);
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateIntMember(componentName + fieldName + "X") +
               GenerateIntMember(componentName + fieldName + "Y");
    }
    private const string k_SnapshotFloat2FieldGetSetTemplate = @"    public float2 Get$(GHOSTFIELD)()
    {
        return new float2($(GHOSTFIELD)X, $(GHOSTFIELD)Y) * $(GHOSTDEQUANT);
    }
    public void Set$(GHOSTFIELD)(float2 val)
    {
        $(GHOSTFIELD)X = (int)(val.x * $(GHOSTQUANT));
        $(GHOSTFIELD)Y = (int)(val.y * $(GHOSTQUANT));
    }
";

    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotFloat2FieldGetSetTemplate
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
        return GenerateInterpolateLerp(componentName + fieldName);
    }
}
class GhostSnapshotValueFloat3 : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(float3);
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateIntMember(componentName + fieldName + "X") +
               GenerateIntMember(componentName + fieldName + "Y") +
               GenerateIntMember(componentName + fieldName + "Z");
    }
    private const string k_SnapshotFloat3FieldGetSetTemplate = @"    public float3 Get$(GHOSTFIELD)()
    {
        return new float3($(GHOSTFIELD)X, $(GHOSTFIELD)Y, $(GHOSTFIELD)Z) * $(GHOSTDEQUANT);
    }
    public void Set$(GHOSTFIELD)(float3 val)
    {
        $(GHOSTFIELD)X = (int)(val.x * $(GHOSTQUANT));
        $(GHOSTFIELD)Y = (int)(val.y * $(GHOSTQUANT));
        $(GHOSTFIELD)Z = (int)(val.z * $(GHOSTQUANT));
    }
";

    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotFloat3FieldGetSetTemplate
                .Replace("$(GHOSTFIELD)", componentName + fieldName)
                .Replace("$(GHOSTQUANT)", quantization.ToString())
                .Replace("$(GHOSTDEQUANT)", (1f/quantization).ToString(CultureInfo.InvariantCulture)+"f");
    }
    public override string GeneratePredict(string componentName, string fieldName)
    {
        return GenerateIntPredict(componentName + fieldName + "X") +
               GenerateIntPredict(componentName + fieldName + "Y") +
               GenerateIntPredict(componentName + fieldName + "Z");
    }
    public override string GenerateRead(string componentName, string fieldName)
    {
        return GenerateIntRead(componentName + fieldName + "X") +
               GenerateIntRead(componentName + fieldName + "Y") +
               GenerateIntRead(componentName + fieldName + "Z");
    }
    public override string GenerateWrite(string componentName, string fieldName)
    {
        return GenerateIntWrite(componentName + fieldName + "X") +
               GenerateIntWrite(componentName + fieldName + "Y") +
               GenerateIntWrite(componentName + fieldName + "Z");
    }
    public override string GenerateInterpolate(string componentName, string fieldName)
    {
        return GenerateInterpolateLerp(componentName + fieldName);
    }
}
class GhostSnapshotValueFloat4 : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(float4);
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateIntMember(componentName + fieldName + "X") +
               GenerateIntMember(componentName + fieldName + "Y") +
               GenerateIntMember(componentName + fieldName + "Z") +
               GenerateIntMember(componentName + fieldName + "W");
    }
    private const string k_SnapshotFloat4FieldGetSetTemplate = @"    public float4 Get$(GHOSTFIELD)()
    {
        return new float4($(GHOSTFIELD)X, $(GHOSTFIELD)Y, $(GHOSTFIELD)Z, $(GHOSTFIELD)W) * $(GHOSTDEQUANT);
    }
    public void Set$(GHOSTFIELD)(float4 val)
    {
        $(GHOSTFIELD)X = (int)(val.x * $(GHOSTQUANT));
        $(GHOSTFIELD)Y = (int)(val.y * $(GHOSTQUANT));
        $(GHOSTFIELD)Z = (int)(val.z * $(GHOSTQUANT));
        $(GHOSTFIELD)W = (int)(val.w * $(GHOSTQUANT));
    }
";

    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotFloat4FieldGetSetTemplate
                .Replace("$(GHOSTFIELD)", componentName + fieldName)
                .Replace("$(GHOSTQUANT)", quantization.ToString())
                .Replace("$(GHOSTDEQUANT)", (1f/quantization).ToString(CultureInfo.InvariantCulture)+"f");
    }
    public override string GeneratePredict(string componentName, string fieldName)
    {
        return GenerateIntPredict(componentName + fieldName + "X") +
               GenerateIntPredict(componentName + fieldName + "Y") +
               GenerateIntPredict(componentName + fieldName + "Z") +
               GenerateIntPredict(componentName + fieldName + "W");
    }
    public override string GenerateRead(string componentName, string fieldName)
    {
        return GenerateIntRead(componentName + fieldName + "X") +
               GenerateIntRead(componentName + fieldName + "Y") +
               GenerateIntRead(componentName + fieldName + "Z") +
               GenerateIntRead(componentName + fieldName + "W");
    }
    public override string GenerateWrite(string componentName, string fieldName)
    {
        return GenerateIntWrite(componentName + fieldName + "X") +
               GenerateIntWrite(componentName + fieldName + "Y") +
               GenerateIntWrite(componentName + fieldName + "Z") +
               GenerateIntWrite(componentName + fieldName + "W");
    }
    public override string GenerateInterpolate(string componentName, string fieldName)
    {
        return GenerateInterpolateLerp(componentName + fieldName);
    }
}
class GhostSnapshotValueInt : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(int);
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateIntMember(componentName + fieldName);
    }

    private const string k_SnapshotIntFieldGetSetTemplate = @"    public int Get$(GHOSTFIELD)()
    {
        return $(GHOSTFIELD);
    }
    public void Set$(GHOSTFIELD)(int val)
    {
        $(GHOSTFIELD) = val;
    }
";
    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotIntFieldGetSetTemplate.Replace("$(GHOSTFIELD)", componentName+fieldName);
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
        return "";
    }
}
class GhostSnapshotValueUInt : GhostSnapshotValue
{
    public override bool CanProcess(System.Type type, string componentName, string fieldName)
    {
        return type == typeof(uint);
    }

    public override string GenerateMembers(string componentName, string fieldName)
    {
        return GenerateUIntMember(componentName + fieldName);
    }

    private const string k_SnapshotIntFieldGetSetTemplate = @"    public uint Get$(GHOSTFIELD)()
    {
        return $(GHOSTFIELD);
    }
    public void Set$(GHOSTFIELD)(uint val)
    {
        $(GHOSTFIELD) = val;
    }
";
    public override string GenerateGetSet(string componentName, string fieldName, int quantization)
    {
        return k_SnapshotIntFieldGetSetTemplate.Replace("$(GHOSTFIELD)", componentName+fieldName);
    }
    public override string GeneratePredict(string componentName, string fieldName)
    {
        return GenerateUIntPredict(componentName + fieldName);
    }
    public override string GenerateRead(string componentName, string fieldName)
    {
        return GenerateUIntRead(componentName + fieldName);
    }
    public override string GenerateWrite(string componentName, string fieldName)
    {
        return GenerateUIntWrite(componentName + fieldName);
    }
    public override string GenerateInterpolate(string componentName, string fieldName)
    {
        return "";
    }
}
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
