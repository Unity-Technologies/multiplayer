using UnityEngine;
using Unity.Entities;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
    public class InputSamplerSystem : ComponentSystem
    {
        public static int spacePresses;
        protected override void OnUpdate()
        {
            if (Input.GetKeyDown("space"))
                ++spacePresses;
        }
    }
    [UpdateInGroup(typeof(SimulationSystemGroup))]
#if !UNITY_SERVER
    [UpdateAfter(typeof(TickClientSimulationSystem))]
#endif
    [UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
    public class InputSamplerResetSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            InputSamplerSystem.spacePresses = 0;
        }
    }

    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateBefore(typeof(AsteroidsCommandSendSystem))]
    // Try to sample input as late as possible
    [UpdateAfter(typeof(GhostSimulationSystemGroup))]
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
        }

        protected override void OnUpdate()
        {
            if (HasSingleton<CommandTargetComponent>() && GetSingleton<CommandTargetComponent>().targetEntity == Entity.Null)
            {
                Entities.WithoutBurst().ForEach((Entity ent, DynamicBuffer<ShipCommandData> data) =>
                {
                    SetSingleton(new CommandTargetComponent {targetEntity = ent});
                }).Run();
            }
            byte left, right, thrust, shoot;
            left = right = thrust = shoot = 0;

            if (World.GetExistingSystem<ClientPresentationSystemGroup>().Enabled)
            {
                if (Input.GetKey("left"))
                    left = 1;
                if (Input.GetKey("right"))
                    right = 1;
                if (Input.GetKey("up"))
                    thrust = 1;
                //if (InputSamplerSystem.spacePresses > 0)
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
                        input.AddCommandData(new ShipCommandData{tick = inputTargetTick, left = left, right = right, thrust = thrust, shoot = shoot});
                    }
                }
            }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}
