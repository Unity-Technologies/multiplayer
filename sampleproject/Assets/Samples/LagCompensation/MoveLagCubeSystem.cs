
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Transforms;

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
public partial class MoveLagCubeSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<LagCompensationSpawner>();
        RequireSingletonForUpdate<NetworkStreamInGame>();
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
