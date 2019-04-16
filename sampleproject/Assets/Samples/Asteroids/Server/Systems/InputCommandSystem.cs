using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    public class InputCommandSystem : JobComponentSystem
    {
        struct PlayerInputJob : IJobProcessComponentDataWithEntity<PlayerStateComponentData>
        {
            public ComponentDataFromEntity<PlayerInputComponentData> shipInput;
            public BufferFromEntity<IncomingCommandDataStreamBufferComponent> cmdBuffer;
            public uint currentTick;
            public unsafe void Execute(Entity entity, int index, [ReadOnly] ref PlayerStateComponentData playerState)
            {
                if (playerState.PlayerShip == Entity.Null)
                {
                    return;
                }

                var buffer = cmdBuffer[entity];
                if (buffer.Length == 0)
                    return;
                DataStreamReader reader = DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*)buffer.GetUnsafePtr(), buffer.Length);
                var ctx = default(DataStreamReader.Context);
                var inputTick = reader.ReadUInt(ref ctx);
                var left = reader.ReadByte(ref ctx);
                var right = reader.ReadByte(ref ctx);
                var thrust = reader.ReadByte(ref ctx);
                var shoot = reader.ReadByte(ref ctx);
                buffer.Clear();
                //Debug.Log("Input delay: " + (int)(currentTick - inputTick));

                // If ship, store commands in network command buffer
                var input = shipInput[playerState.PlayerShip];
                input.mostRecentPos = (input.mostRecentPos + 1) % 32;
                input.tick[input.mostRecentPos] = inputTick;
                input.left[input.mostRecentPos] = left;
                input.right[input.mostRecentPos] = right;
                input.thrust[input.mostRecentPos] = thrust;
                input.shoot[input.mostRecentPos] = shoot;
                shipInput[playerState.PlayerShip] = input;
            }
        }

        private ServerSimulationSystemGroup serverSimulationSystemGroup;
        protected override void OnCreateManager()
        {
            serverSimulationSystemGroup = World.GetExistingManager<ServerSimulationSystemGroup>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var playerJob = new PlayerInputJob();
            playerJob.shipInput = GetComponentDataFromEntity<PlayerInputComponentData>();
            playerJob.cmdBuffer = GetBufferFromEntity<IncomingCommandDataStreamBufferComponent>();
            playerJob.currentTick = serverSimulationSystemGroup.ServerTick;
            return playerJob.ScheduleSingle(this, inputDeps);
        }
    }
}
