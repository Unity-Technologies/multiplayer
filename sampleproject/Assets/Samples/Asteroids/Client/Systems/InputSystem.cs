using UnityEngine;
using Unity.Entities;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial class InputSystem : SystemBase
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
            if (isThinClient)
            {
                if (TryGetSingleton<CommandTargetComponent>(out var commandTarget))
                {
                    if (commandTarget.targetEntity == Entity.Null ||
                        !EntityManager.HasComponent<ShipCommandData>(commandTarget.targetEntity))
                    {
                        // No ghosts are spawned, so create a placeholder struct to store the commands in
                        var ent = EntityManager.CreateEntity();
                        EntityManager.AddBuffer<ShipCommandData>(ent);
                        SetSingleton(new CommandTargetComponent{targetEntity = ent});
                    }
                }
            }
            byte left, right, thrust, shoot;
            left = right = thrust = shoot = 0;

            if (!isThinClient)
            {
                if (Input.GetKey("left") || TouchInput.GetKey(TouchInput.KeyCode.Left))
                    left = 1;
                if (Input.GetKey("right") || TouchInput.GetKey(TouchInput.KeyCode.Right))
                    right = 1;
                if (Input.GetKey("up") || TouchInput.GetKey(TouchInput.KeyCode.Up))
                    thrust = 1;
                if (Input.GetKey("space") || TouchInput.GetKey(TouchInput.KeyCode.Space))
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

            var commandBuffer = m_Barrier.CreateCommandBuffer();
            var inputFromEntity = GetBufferFromEntity<ShipCommandData>();
            var inputTargetTick = m_ClientSimulationSystemGroup.ServerTick;
            TryGetSingletonEntity<ShipCommandData>(out var targetEntity);
            Job.WithCode(() => {
                if (isThinClient && shoot != 0)
                {
                    // Special handling for thin clients since we can't tell if the ship is spawned or not
                    var req = commandBuffer.CreateEntity();
                    commandBuffer.AddComponent<PlayerSpawnRequest>(req);
                    commandBuffer.AddComponent(req, new SendRpcCommandRequestComponent());
                }
                if (targetEntity == Entity.Null)
                {
                    if (shoot != 0)
                    {
                        var req = commandBuffer.CreateEntity();
                        commandBuffer.AddComponent<PlayerSpawnRequest>(req);
                        commandBuffer.AddComponent(req, new SendRpcCommandRequestComponent());
                    }
                }
                else
                {
                    // If ship, store commands in network command buffer
                    if (inputFromEntity.HasComponent(targetEntity))
                    {
                        var input = inputFromEntity[targetEntity];
                        input.AddCommandData(new ShipCommandData{Tick = inputTargetTick, left = left, right = right, thrust = thrust, shoot = shoot});
                    }
                }
            }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}
