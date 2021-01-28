using Unity.Transforms;
using Unity.Mathematics;
using Unity.NetCode;

namespace Asteroids.Mixed
{
    [GhostComponentVariation(typeof(Translation), "Translation - 2D")]
    [GhostComponent(PrefabType = GhostPrefabType.All, OwnerPredictedSendType = GhostSendType.All, SendDataForChildEntity = false)]
    public struct Translation2d
    {
        //Will serialize just x,y positions
        [GhostField(Quantization=1000, Smoothing=SmoothingAction.InterpolateAndExtrapolate, SubType=SubTypes.Float3_XY)] public float3 Value;
    }

    [GhostComponentVariation(typeof(Rotation), "Rotation - 2D")]
    [GhostComponent(PrefabType = GhostPrefabType.All, OwnerPredictedSendType = GhostSendType.All, SendDataForChildEntity = false)]
    public struct Rotation2d
    {
        //Will serialize just the one angle
        [GhostField(Quantization=1000, Smoothing=SmoothingAction.InterpolateAndExtrapolate, SubType=SubTypes.Rotation2D)] public quaternion Value;
    }
}
