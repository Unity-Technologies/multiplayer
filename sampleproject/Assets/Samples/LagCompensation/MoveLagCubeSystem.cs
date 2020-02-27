
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Transforms;

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
public class MoveLagCubeSystem : JobComponentSystem
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableLagCompensationGhostSendSystemComponent>();
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return Entities.WithAll<PredictedGhostComponent>().ForEach((ref Translation trans) =>
        {
            if (trans.Value.x >= 5)
                trans.Value.x = -5;
            else
                trans.Value.x += 0.1f;
        }).Schedule(inputDeps);
    }
}
