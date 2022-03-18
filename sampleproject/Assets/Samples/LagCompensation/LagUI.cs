using UnityEngine;
using UnityEngine.UI;
using Unity.NetCode;
using Unity.Entities;
using Unity.Collections;

public class LagUI : MonoBehaviour
{
    public static bool EnableLagCompensation = true;
    public static uint ClientTick;
    public static uint ServerTick;
    public static bool ClientHit;
    public static bool ServerHit;

    public Toggle LagToggle;
    public Text ClientStatus;
    public Text ServerStatus;
    private void Update()
    {
        EnableLagCompensation = LagToggle.isOn;
        ClientStatus.text = $"Client: {(ClientHit?"hit":"miss")} @ tick {ClientTick}";
        ServerStatus.text = $"Server: {(ServerHit?"hit":"miss")} @ tick {ServerTick}";
    }
}

public struct LagHitStatus : IRpcCommand
{
    public uint Tick;
    public bool Hit;
    public bool IsServer;
}

public struct ToggleLagCompensationRequest : IRpcCommand
{
    public bool Enable;
    public Entity Player;
}

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public partial class LagUISystem : SystemBase
{
    private bool m_prevEnabled = false;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<NetworkIdComponent>();
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
[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
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
