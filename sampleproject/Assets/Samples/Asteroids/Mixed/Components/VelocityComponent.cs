using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

[GhostDefaultComponent(GhostDefaultComponentAttribute.Type.Server|GhostDefaultComponentAttribute.Type.PredictedClient)]
public struct Velocity : IComponentData
{
    [GhostDefaultField(100, true)]
    public float2 Value;
}
