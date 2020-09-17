
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Transforms;

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
public class MoveLagCubeSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableLagCompensationGame>();
    }
    protected override void OnUpdate()
    {
        Entities.WithAll<PredictedGhostComponent>().ForEach((ref Translation trans) =>
        {
            if (trans.Value.x >= 5)
                trans.Value.x = -5;
            else
                trans.Value.x += 0.1f;
        }).Schedule();
    }
}
