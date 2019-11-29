using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
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
    public class InputSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private GhostPredictionSystemGroup m_GhostPredict;
        private int frameCount;

        protected override void OnCreate()
        {
            m_GhostPredict = World.GetOrCreateSystem<GhostPredictionSystemGroup>();
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        [ExcludeComponent(typeof(NetworkStreamDisconnected))]
        [RequireComponentTag(typeof(OutgoingRpcDataStreamBufferComponent))]
        struct PlayerInputJob : IJobForEachWithEntity<CommandTargetComponent>
        {
            public byte left;
            public byte right;
            public byte thrust;
            public byte shoot;
            public EntityCommandBuffer.Concurrent commandBuffer;
            public BufferFromEntity<ShipCommandData> inputFromEntity;
            public uint inputTargetTick;
            public void Execute(Entity entity, int index, [ReadOnly] ref CommandTargetComponent state)
            {
                if (state.targetEntity == Entity.Null)
                {
                    if (shoot != 0)
                    {
                        var req = commandBuffer.CreateEntity(index);
                        commandBuffer.AddComponent<PlayerSpawnRequest>(index, req);
                        commandBuffer.AddComponent(index, req, new SendRpcCommandRequestComponent {TargetConnection = entity});
                    }
                }
                else
                {
                    // If ship, store commands in network command buffer
                    if (inputFromEntity.Exists(state.targetEntity))
                    {
                        var input = inputFromEntity[state.targetEntity];
                        input.AddCommandData(new ShipCommandData{tick = inputTargetTick, left = left, right = right, thrust = thrust, shoot = shoot});
                    }
                }
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var playerJob = new PlayerInputJob();
            playerJob.left = 0;
            playerJob.right = 0;
            playerJob.thrust = 0;
            playerJob.shoot = 0;
            playerJob.commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
            playerJob.inputFromEntity = GetBufferFromEntity<ShipCommandData>();
            playerJob.inputTargetTick = m_GhostPredict.PredictingTick;

            if (World.GetExistingSystem<ClientPresentationSystemGroup>().Enabled)
            {
                if (Input.GetKey("left"))
                    playerJob.left = 1;
                if (Input.GetKey("right"))
                    playerJob.right = 1;
                if (Input.GetKey("up"))
                    playerJob.thrust = 1;
                //if (InputSamplerSystem.spacePresses > 0)
                if (Input.GetKey("space"))
                    playerJob.shoot = 1;
            }
            else
            {
                // Spawn and generate some random inputs
                var state = (int) Time.ElapsedTime % 3;
                if (state == 0)
                    playerJob.left = 1;
                else
                    playerJob.thrust = 1;
                ++frameCount;
                if (frameCount % 100 == 0)
                {
                    playerJob.shoot = 1;
                    frameCount = 0;
                }
            }

            var handle = playerJob.ScheduleSingle(this, inputDeps);
            m_Barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}
