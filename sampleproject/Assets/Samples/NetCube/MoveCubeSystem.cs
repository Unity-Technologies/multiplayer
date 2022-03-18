using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

[UpdateInGroup(typeof(GhostPredictionSystemGroup))]
public partial class MoveCubeSystem : SystemBase
{
    GhostPredictionSystemGroup m_GhostPredictionSystemGroup;
    protected override void OnCreate()
    {
        m_GhostPredictionSystemGroup = World.GetExistingSystem<GhostPredictionSystemGroup>();
    }
    protected override void OnUpdate()
    {
        var tick = m_GhostPredictionSystemGroup.PredictingTick;
        var fixedCubeSpeed = Time.DeltaTime * 3;
        Entities.ForEach((DynamicBuffer<CubeInput> inputBuffer, ref Translation trans, in PredictedGhostComponent prediction) =>
        {
            if (!GhostPredictionSystemGroup.ShouldPredict(tick, prediction))
                return;
            inputBuffer.GetDataAtTick(tick, out var input);
            if (input.horizontal > 0)
                trans.Value.x += fixedCubeSpeed;
            if (input.horizontal < 0)
                trans.Value.x -= fixedCubeSpeed;
            if (input.vertical > 0)
                trans.Value.z += fixedCubeSpeed;
            if (input.vertical < 0)
                trans.Value.z -= fixedCubeSpeed;
        }).ScheduleParallel();
    }
}
