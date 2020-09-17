using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Jobs;
using Unity.Burst;

[GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
public struct RayTraceCommand : ICommandData
{
    public uint Tick {get; set;}
    public float3 origin;
    public float3 direction;
    public uint lastFire;
}


[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(GhostSimulationSystemGroup))]
[AlwaysSynchronizeSystem]
public class SampleRayTraceCommandSystem : SystemBase
{
    ClientSimulationSystemGroup m_systemGroup;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableLagCompensationGame>();
        RequireSingletonForUpdate<CommandTargetComponent>();
        m_systemGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
    }
    protected override void OnUpdate()
    {
        var target = GetSingleton<CommandTargetComponent>();
        if (target.targetEntity == Entity.Null)
        {
            Entities.WithoutBurst().WithAll<PredictedGhostComponent>().ForEach((Entity entity, in LagPlayer player) => {
                target.targetEntity = entity;
                SetSingleton(target);
            }).Run();
        }
        if (target.targetEntity == Entity.Null || m_systemGroup.ServerTick == 0 || !EntityManager.HasComponent<RayTraceCommand>(target.targetEntity))
            return;
        var buffer = EntityManager.GetBuffer<RayTraceCommand>(target.targetEntity);
        var cmd = default(RayTraceCommand);
        cmd.Tick = m_systemGroup.ServerTick;
        if (UnityEngine.Input.GetMouseButtonDown(0))
        {
            var ray = UnityEngine.Camera.main.ScreenPointToRay(UnityEngine.Input.mousePosition);
            cmd.origin = ray.origin;
            cmd.direction = ray.direction;
            cmd.lastFire = cmd.Tick;
        }
        // Not firing and data for the tick already exists, skip it to make sure a command is not overwritten
        else if (buffer.GetDataAtTick(cmd.Tick, out var dupCmd) && dupCmd.Tick == cmd.Tick)
            return;
        buffer.AddCommandData(cmd);
    }
}
