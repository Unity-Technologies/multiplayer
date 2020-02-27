using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Jobs;

public struct RayTraceCommand : ICommandData<RayTraceCommand>
{
    public uint Tick => tick;
    public uint tick;
    public float3 origin;
    public float3 direction;
    public uint lastFire;

    public void Serialize(ref DataStreamWriter writer)
    {
        writer.WriteFloat(origin.x);
        writer.WriteFloat(origin.y);
        writer.WriteFloat(origin.z);
        writer.WriteFloat(direction.x);
        writer.WriteFloat(direction.y);
        writer.WriteFloat(direction.z);
        writer.WriteUInt(lastFire);
    }
    public void Serialize(ref DataStreamWriter writer, RayTraceCommand baseline, NetworkCompressionModel model)
    {
        Serialize(ref writer);
    }
    public void Deserialize(uint t, ref DataStreamReader reader)
    {
        tick = t;
        origin.x = reader.ReadFloat();
        origin.y = reader.ReadFloat();
        origin.z = reader.ReadFloat();
        direction.x = reader.ReadFloat();
        direction.y = reader.ReadFloat();
        direction.z = reader.ReadFloat();
        lastFire = reader.ReadUInt();
    }
    public void Deserialize(uint t, ref DataStreamReader reader, RayTraceCommand baseline, NetworkCompressionModel model)
    {
        Deserialize(t, ref reader);
    }
}
public class RayTraceCommandSendSystem : CommandSendSystem<RayTraceCommand>
{}
public class RayTraceCommandReceiveSystem : CommandReceiveSystem<RayTraceCommand>
{}


[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(GhostSimulationSystemGroup))]
public class SampleRayTraceCommandSystem : JobComponentSystem
{
    ClientSimulationSystemGroup m_systemGroup;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableLagCompensationGhostReceiveSystemComponent>();
        RequireSingletonForUpdate<CommandTargetComponent>();
        m_systemGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        inputDeps.Complete();
        var target = GetSingleton<CommandTargetComponent>();
        if (target.targetEntity == Entity.Null)
        {
            Entities.WithoutBurst().WithAll<PredictedGhostComponent>().ForEach((Entity entity, in LagPlayer player) => {
                target.targetEntity = entity;
                SetSingleton(target);
            }).Run();
        }
        if (target.targetEntity == Entity.Null || m_systemGroup.ServerTick == 0 || !EntityManager.HasComponent<RayTraceCommand>(target.targetEntity))
            return default;
        var buffer = EntityManager.GetBuffer<RayTraceCommand>(target.targetEntity);
        var cmd = default(RayTraceCommand);
        cmd.tick = m_systemGroup.ServerTick;
        if (UnityEngine.Input.GetMouseButtonDown(0))
        {
            var ray = UnityEngine.Camera.main.ScreenPointToRay(UnityEngine.Input.mousePosition);
            cmd.origin = ray.origin;
            cmd.direction = ray.direction;
            cmd.lastFire = cmd.tick;
        }
        // Not firing and data for the tick already exists, skip it to make sure a fiew command is not overwritten
        else if (buffer.GetDataAtTick(cmd.tick, out var dupCmd) && dupCmd.Tick == cmd.tick)
            return default;
        buffer.AddCommandData(cmd);
        return default;
    }
}
