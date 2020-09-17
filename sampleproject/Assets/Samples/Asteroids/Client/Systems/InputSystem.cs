using UnityEngine;
using Unity.Entities;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateBefore(typeof(CommandSendSystemGroup))]
    [UpdateBefore(typeof(GhostSimulationSystemGroup))]
    public class InputSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private ClientSimulationSystemGroup m_ClientSimulationSystemGroup;
        private int m_FrameCount;

        protected override void OnCreate()
        {
            m_ClientSimulationSystemGroup = World.GetOrCreateSystem<ClientSimulationSystemGroup>();
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            RequireSingletonForUpdate<NetworkStreamInGame>();
            // Just to make sure this system does not run in other scenes
            RequireSingletonForUpdate<LevelComponent>();
        }

        protected override void OnUpdate()
        {
            bool isThinClient = HasSingleton<ThinClientComponent>();
            if (HasSingleton<CommandTargetComponent>() && GetSingleton<CommandTargetComponent>().targetEntity == Entity.Null)
            {
                if (isThinClient)
                {
                    // No ghosts are spawned, so create a placeholder struct to store the commands in
                    var ent = EntityManager.CreateEntity();
                    EntityManager.AddBuffer<ShipCommandData>(ent);
                    SetSingleton(new CommandTargetComponent{targetEntity = ent});
                }
                else
                {
                    Entities.WithoutBurst().ForEach((Entity ent, DynamicBuffer<ShipCommandData> data) =>
                    {
                        SetSingleton(new CommandTargetComponent {targetEntity = ent});
                    }).Run();
                }
            }
            byte left, right, thrust, shoot;
            left = right = thrust = shoot = 0;

            if (!isThinClient)
            {
                if (Input.GetKey("left"))
                    left = 1;
                if (Input.GetKey("right"))
                    right = 1;
                if (Input.GetKey("up"))
                    thrust = 1;
                if (Input.GetKey("space"))
                    shoot = 1;
            }
            else
            {
                // Spawn and generate some random inputs
                var state = (int) Time.ElapsedTime % 3;
                if (state == 0)
                    left = 1;
                else
                    thrust = 1;
                ++m_FrameCount;
                if (m_FrameCount % 100 == 0)
                {
                    shoot = 1;
                    m_FrameCount = 0;
                }
            }

            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var inputFromEntity = GetBufferFromEntity<ShipCommandData>();
            var inputTargetTick = m_ClientSimulationSystemGroup.ServerTick;
            Entities.WithAll<OutgoingRpcDataStreamBufferComponent>().WithNone<NetworkStreamDisconnected>()
                .ForEach((Entity entity, int nativeThreadIndex, in CommandTargetComponent state) =>
            {
                if (isThinClient && shoot != 0)
                {
                    // Special handling for thin clients since we can't tell if the ship is spawned or not
                    var req = commandBuffer.CreateEntity(nativeThreadIndex);
                    commandBuffer.AddComponent<PlayerSpawnRequest>(nativeThreadIndex, req);
                    commandBuffer.AddComponent(nativeThreadIndex, req, new SendRpcCommandRequestComponent {TargetConnection = entity});
                }
                if (state.targetEntity == Entity.Null)
                {
                    if (shoot != 0)
                    {
                        var req = commandBuffer.CreateEntity(nativeThreadIndex);
                        commandBuffer.AddComponent<PlayerSpawnRequest>(nativeThreadIndex, req);
                        commandBuffer.AddComponent(nativeThreadIndex, req, new SendRpcCommandRequestComponent {TargetConnection = entity});
                    }
                }
                else
                {
                    // If ship, store commands in network command buffer
                    if (inputFromEntity.HasComponent(state.targetEntity))
                    {
                        var input = inputFromEntity[state.targetEntity];
                        input.AddCommandData(new ShipCommandData{Tick = inputTargetTick, left = left, right = right, thrust = thrust, shoot = shoot});
                    }
                }
            }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}
