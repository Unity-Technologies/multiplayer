using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

[GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
public struct RayTraceCommand : ICommandData
{
    public NetworkTick Tick {get; set;}
    public float3 origin;
    public float3 direction;
    public NetworkTick lastFire;
}


[UpdateInGroup(typeof(GhostInputSystemGroup))]
[AlwaysSynchronizeSystem]
public partial class SampleRayTraceCommandSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<LagCompensationSpawner>();
    }
    protected override void OnUpdate()
    {
        var networkTime = GetSingleton<NetworkTime>();
        if (!TryGetSingletonEntity<RayTraceCommand>(out var targetEntity) || !networkTime.ServerTick.IsValid)
            return;
        var buffer = EntityManager.GetBuffer<RayTraceCommand>(targetEntity);
        var cmd = default(RayTraceCommand);
        cmd.Tick = networkTime.ServerTick;
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
