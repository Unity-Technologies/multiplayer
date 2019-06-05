using System;
using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
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
    [UpdateAfter(typeof(GhostReceiveSystemGroup))]
    public class InputSystem : JobComponentSystem
    {
        private RpcQueue<RpcSpawn> m_RpcQueue;
        protected override void OnCreateManager()
        {
            m_RpcQueue = World.GetOrCreateSystem<MultiplayerSampleRpcSystem>().GetRpcQueue<RpcSpawn>();
        }

        [ExcludeComponent(typeof(NetworkStreamDisconnected))]
        struct PlayerInputJob : IJobForEachWithEntity<CommandTargetComponent>
        {
            public byte left;
            public byte right;
            public byte thrust;
            public byte shoot;
            public ComponentDataFromEntity<ShipStateComponentData> shipState;
            public RpcQueue<RpcSpawn> rpcQueue;
            public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> rpcBuffer;
            public BufferFromEntity<ShipCommandData> inputFromEntity;
            public uint inputTargetTick;
            public void Execute(Entity entity, int index, [ReadOnly] ref CommandTargetComponent state)
            {
                if (state.targetEntity == Entity.Null)
                {
                    if (shoot != 0)
                    {
                        rpcQueue.Schedule(rpcBuffer[entity], new RpcSpawn());
                    }
                }
                else
                {
                    // If ship, store commands in network command buffer
                    // FIXME: when destroying the ship is in a command buffer this no longer works
                    if (shipState.Exists(state.targetEntity)) // There might be a pending set to null
                        shipState[state.targetEntity] = new ShipStateComponentData(thrust, true);
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
            playerJob.shipState = GetComponentDataFromEntity<ShipStateComponentData>();
            playerJob.rpcQueue = m_RpcQueue;
            playerJob.rpcBuffer = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>();
            playerJob.inputFromEntity = GetBufferFromEntity<ShipCommandData>();
            playerJob.inputTargetTick = NetworkTimeSystem.predictTargetTick;

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
                var topGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
                // Spawn and generate some random inputs
                var state = (int) topGroup.UpdateTime % 3;
                if (state == 0)
                    playerJob.left = 1;
                else
                    playerJob.thrust = 1;
                if (Time.frameCount % 100 == 0)
                    playerJob.shoot = 1;
            }

            return playerJob.ScheduleSingle(this, inputDeps);
        }
    }
}
