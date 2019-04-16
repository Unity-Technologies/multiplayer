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
    [UpdateBefore(typeof(NetworkStreamSendSystem))]
    // FIXME: dependency just for acking
    [UpdateAfter(typeof(GhostReceiveSystemGroup))]
    public class InputSystem : JobComponentSystem
    {
        private RpcQueue<RpcSpawn> m_RpcQueue;
        protected override void OnCreateManager()
        {
            m_RpcQueue = World.GetOrCreateManager<RpcSystem>().GetRpcQueue<RpcSpawn>();
        }

        [ExcludeComponent(typeof(NetworkStreamDisconnected))]
        struct PlayerInputJob : IJobProcessComponentDataWithEntity<PlayerStateComponentData>
        {
            public byte left;
            public byte right;
            public byte thrust;
            public byte shoot;
            public ComponentDataFromEntity<ShipStateComponentData> shipState;
            public RpcQueue<RpcSpawn> rpcQueue;
            public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> rpcBuffer;
            public BufferFromEntity<OutgoingCommandDataStreamBufferComponent> cmdBuffer;
            public ComponentDataFromEntity<NetworkSnapshotAck> ackSnapshot;
            public uint localTime;
            public uint inputTargetTick;
            public unsafe void Execute(Entity entity, int index, [ReadOnly] ref PlayerStateComponentData state)
            {
                // FIXME: ack and sending command stream should be handled by a different system
                DataStreamWriter writer = new DataStreamWriter(128, Allocator.Temp);
                var buffer = cmdBuffer[entity];
                var ack = ackSnapshot[entity];
                writer.Write((byte)NetworkStreamProtocol.Command);
                writer.Write(ack.LastReceivedSnapshotByLocal);
                writer.Write(ack.ReceivedSnapshotByLocalMask);
                writer.Write(localTime);
                writer.Write(ack.LastReceivedRemoteTime - (localTime - ack.LastReceiveTimestamp));
                if (state.PlayerShip == Entity.Null)
                {
                    if (shoot != 0)
                    {
                        rpcQueue.Schedule(rpcBuffer[entity], new RpcSpawn());
                    }
                }
                else
                {
                    writer.Write(inputTargetTick);
                    writer.Write(left);
                    writer.Write(right);
                    writer.Write(thrust);
                    writer.Write(shoot);

                    // If ship, store commands in network command buffer
                    /*input = new PlayerInputComponentData(left, right, thrust, shoot);*/
                    // FIXME: when destroying the ship is in a command buffer this no longer works
                    if (shipState.Exists(state.PlayerShip)) // There might be a pending set to null
                        shipState[state.PlayerShip] = new ShipStateComponentData(thrust, true);
                }
                buffer.ResizeUninitialized(writer.Length);
                byte* ptr = (byte*) buffer.GetUnsafePtr();
                UnsafeUtility.MemCpy(buffer.GetUnsafePtr(), writer.GetUnsafeReadOnlyPtr(), writer.Length);
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
            playerJob.cmdBuffer = GetBufferFromEntity<OutgoingCommandDataStreamBufferComponent>();
            playerJob.ackSnapshot = GetComponentDataFromEntity<NetworkSnapshotAck>();
            playerJob.localTime = NetworkTimeSystem.TimestampMS;
            playerJob.inputTargetTick = NetworkTimeSystem.predictTargetTick;

            if (World.GetExistingManager<ClientPresentationSystemGroup>().Enabled)
            {
                if (Input.GetKey("left"))
                    playerJob.left = 1;
                if (Input.GetKey("right"))
                    playerJob.right = 1;
                if (Input.GetKey("up"))
                    playerJob.thrust = 1;
                if (InputSamplerSystem.spacePresses > 0)
                    //if (Input.GetKey("space"))
                    playerJob.shoot = 1;
            }
            else
            {
                // Spawn and generate some random inputs
                var state = (int) Time.fixedTime % 3;
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
