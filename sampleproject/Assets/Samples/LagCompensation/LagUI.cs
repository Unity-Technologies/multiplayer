using UnityEngine;
using UnityEngine.UI;
using Unity.NetCode;
using Unity.Entities;
using Unity.Collections;

public class LagUI : MonoBehaviour
{
    public static bool EnableLagCompensation = true;
    public static NetworkTick ClientTick;
    public static NetworkTick ServerTick;
    public static bool ClientHit;
    public static bool ServerHit;

    public Toggle LagToggle;
    public Text ClientStatus;
    public Text ServerStatus;
    private void Update()
    {
        EnableLagCompensation = LagToggle.isOn;
        ClientStatus.text = $"Client: {(ClientHit?"hit":"miss")} @ tick {ClientTick.ToFixedString()}";
        ServerStatus.text = $"Server: {(ServerHit?"hit":"miss")} @ tick {ServerTick.ToFixedString()}";
    }
}

public struct LagHitStatus : IRpcCommand
{
    public NetworkTick Tick;
    public bool Hit;
    public bool IsServer;
}

public struct ToggleLagCompensationRequest : IRpcCommand
{
    public bool Enable;
    public Entity Player;
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class LagUISystem : SystemBase
{
    private bool m_prevEnabled = false;
    protected override void OnCreate()
    {
        RequireForUpdate<NetworkIdComponent>();
    }
    protected override void OnUpdate()
    {
        if (m_prevEnabled != LagUI.EnableLagCompensation && TryGetSingletonEntity<RayTraceCommand>(out var player))
        {
            m_prevEnabled = LagUI.EnableLagCompensation;
            var ent = EntityManager.CreateEntity();
            EntityManager.AddComponentData(ent, new ToggleLagCompensationRequest{Enable = m_prevEnabled, Player = player});
            EntityManager.AddComponentData(ent, default(SendRpcCommandRequestComponent));
        }
        var cmdBuffer = new EntityCommandBuffer(Allocator.Temp);
        Entities
            .WithoutBurst()
            .ForEach((Entity entity, in LagHitStatus status) => {
            if (status.IsServer)
            {
                LagUI.ServerTick = status.Tick;
                LagUI.ServerHit = status.Hit;
            }
            else
            {
                LagUI.ClientTick = status.Tick;
                LagUI.ClientHit = status.Hit;
            }
            cmdBuffer.DestroyEntity(entity);
        }).Run();
        cmdBuffer.Playback(EntityManager);
    }
}
[RequireMatchingQueriesForUpdate]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial class LagUIControlSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // Process requests to toggle lag compensation
        var cmdBuffer = new EntityCommandBuffer(Allocator.Temp);
        Entities
            .WithoutBurst()
            .ForEach((Entity entity, in ToggleLagCompensationRequest toggle, in ReceiveRpcCommandRequestComponent req) =>
        {
            // Find the correct control entity
            if (!toggle.Enable && EntityManager.HasComponent<LagCompensationEnabled>(toggle.Player))
                cmdBuffer.RemoveComponent<LagCompensationEnabled>(toggle.Player);
            else if (toggle.Enable && !EntityManager.HasComponent<LagCompensationEnabled>(toggle.Player))
                cmdBuffer.AddComponent<LagCompensationEnabled>(toggle.Player);
            cmdBuffer.DestroyEntity(entity);
        }).Run();
        cmdBuffer.Playback(EntityManager);
    }
}
