using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

[GhostComponent(PrefabType = GhostPrefabType.AllPredicted, OwnerPredictedSendType = GhostSendType.Predicted)]
public struct Velocity : IComponentData
{
    [GhostField(Quantization=100, Interpolate=true)]
    public float2 Value;
}
