using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Asteroids.Client
{
    [UpdateAfter(typeof(RenderInterpolationSystem))]
    [UpdateBefore(typeof(ParticleEmitterSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ShipThrustParticleSystem : JobComponentSystem
    {
        [BurstCompile]
        struct ThrustJob : IJobProcessComponentData<ParticleEmitterComponentData, ShipStateComponentData>
        {
            public void Execute(ref ParticleEmitterComponentData emitter, [ReadOnly] ref ShipStateComponentData state)
            {
                emitter.active = state.State;
            }
        }

        override protected JobHandle OnUpdate(JobHandle inputDeps)
        {
            var thrustJob = new ThrustJob();
            return thrustJob.Schedule(this, inputDeps);
        }
    }

    [UpdateAfter(typeof(RenderInterpolationSystem))]
    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ShipTrackingSystem : JobComponentSystem
    {
        private ComponentGroup shipGroup;
        private ComponentGroup m_LevelGroup;
        private NativeArray<int> teleport;
        protected override void OnCreateManager()
        {
            shipGroup = GetComponentGroup(
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<Rotation>(),
                ComponentType.ReadOnly<ShipStateComponentData>(),
                ComponentType.ReadOnly<ShipTagComponentData>());
            teleport = new NativeArray<int>(1, Allocator.Persistent);
            teleport[0] = 1;
            m_LevelGroup = GetComponentGroup(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
        }

        protected override void OnDestroyManager()
        {
            teleport.Dispose();
        }

        [BurstCompile]
        struct ChunkTrackJob : IJobProcessComponentData<LineRendererComponentData>
        {
            [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk> shipChunks;
            [ReadOnly] public ArchetypeChunkComponentType<Translation> positionType;
            [ReadOnly] public ArchetypeChunkComponentType<ShipStateComponentData> stateType;

            public int screenWidth;
            public int screenHeight;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<LevelComponent> level;

            public NativeArray<int> teleport;

            public void Execute(ref LineRendererComponentData target)
            {
                int mapWidth = level[0].width;
                int mapHeight = level[0].height;
                int nextTeleport = 1;
                for (int i = 0; i < shipChunks.Length; ++i)
                {
                    var position = shipChunks[i].GetNativeArray(positionType);
                    var state = shipChunks[i].GetNativeArray(stateType);
                    for (int ship = 0; ship < position.Length; ++ship)
                    {
                        if (state[ship].IsLocalPlayer != 0)
                        {
                            float3 pos = position[ship].Value;
                            pos.x -= screenWidth / 2;
                            pos.y -= screenHeight / 2;
                            if (pos.x + screenWidth > mapWidth)
                                pos.x = mapWidth - screenWidth;
                            if (pos.y + screenHeight > mapHeight)
                                pos.y = mapHeight - screenHeight;
                            if (pos.x < 0)
                                pos.x = 0;
                            if (pos.y < 0)
                                pos.y = 0;
                            target.targetOffset = pos.xy;
                            target.teleport = teleport[0];
                            nextTeleport = 0;
                        }
                    }
                }
                teleport[0] = nextTeleport;
            }
        }
        override protected JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (shipGroup.IsEmptyIgnoreFilter)
                return inputDeps;
            var trackJob = new ChunkTrackJob();
            JobHandle gatherJob, levelHandle;
            trackJob.shipChunks = shipGroup.CreateArchetypeChunkArray(Allocator.TempJob, out gatherJob);
            trackJob.positionType = GetArchetypeChunkComponentType<Translation>(true);
            trackJob.stateType = GetArchetypeChunkComponentType<ShipStateComponentData>(true);
            trackJob.screenWidth = Screen.width;
            trackJob.screenHeight = Screen.height;
            trackJob.level = m_LevelGroup.ToComponentDataArray<LevelComponent>(Allocator.TempJob, out levelHandle);
            trackJob.teleport = teleport;
            return trackJob.ScheduleSingle(this, JobHandle.CombineDependencies(inputDeps, gatherJob, levelHandle));
        }
    }

    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateAfter(typeof(RenderInterpolationSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ShipRenderSystem : JobComponentSystem
    {
        private ComponentGroup lineGroup;
        private NativeQueue<LineRenderSystem.Line>.Concurrent lineQueue;
        protected override void OnCreateManager()
        {
            lineGroup = GetComponentGroup(ComponentType.ReadWrite<LineRendererComponentData>());
            lineQueue = World.GetOrCreateManager<LineRenderSystem>().LineQueue;
        }

        [BurstCompile]
        [RequireComponentTag(typeof(ShipTagComponentData))]
        struct ChunkRenderJob : IJobProcessComponentData<Translation, Rotation>
        {
            public NativeQueue<LineRenderSystem.Line>.Concurrent lines;
            public float4 shipColor;
            public float3 shipTop;
            public float3 shipBL;
            public float3 shipBR;
            public float shipLineWidth;

            public void Execute([ReadOnly] ref Translation position, [ReadOnly] ref Rotation rotation)
            {
                float3 pos = position.Value;
                var rot = rotation.Value;

                var rotTop = pos + math.mul(rot, shipTop);
                var rotBL = pos + math.mul(rot, shipBL);
                var rotBR = pos + math.mul(rot, shipBR);
                lines.Enqueue(new LineRenderSystem.Line(rotTop.xy, rotBL.xy, shipColor, shipLineWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotTop.xy, rotBR.xy, shipColor, shipLineWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotBL.xy, rotBR.xy, shipColor, shipLineWidth));
            }
        }
        override protected JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (lineGroup.IsEmptyIgnoreFilter)
                return inputDeps;
            var rendJob = new ChunkRenderJob();
            rendJob.lines = lineQueue;

            float shipWidth = 10;
            float shipHeight = 20;
            rendJob.shipLineWidth = 2;
            rendJob.shipColor = new float4(0.85f, 0.85f, 0.85f, 1);
            rendJob.shipTop = new float3(0, shipHeight / 2, 0);
            rendJob.shipBL = new float3(-shipWidth / 2, -shipHeight / 2, 0);
            rendJob.shipBR = new float3(shipWidth / 2, -shipHeight / 2, 0);

            return rendJob.Schedule(this, inputDeps);
        }
    }
}
