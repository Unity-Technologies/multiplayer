using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateBefore(typeof(ParticleEmitterSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ShipThrustParticleSystem : SystemBase
    {
        override protected void OnUpdate()
        {
            Entities.ForEach((ref ParticleEmitterComponentData emitter, in ShipStateComponentData state) =>
            {
                emitter.active = state.State;
            }).ScheduleParallel();
        }
    }

    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ShipTrackingSystem : SystemBase
    {
        private EntityQuery m_ShipGroup;
        private EntityQuery m_LevelGroup;
        private NativeArray<int> m_Teleport;

        protected override void OnCreate()
        {
            m_ShipGroup = GetEntityQuery(
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<Rotation>(),
                ComponentType.ReadOnly<ShipStateComponentData>(),
                ComponentType.ReadOnly<ShipTagComponentData>());
            m_Teleport = new NativeArray<int>(1, Allocator.Persistent);
            m_Teleport[0] = 1;
            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
        }

        protected override void OnDestroy()
        {
            m_Teleport.Dispose();
        }

        override protected void OnUpdate()
        {
            if (m_ShipGroup.IsEmptyIgnoreFilter)
                return;

            JobHandle levelHandle;
            var localPlayerShip = GetSingleton<CommandTargetComponent>().targetEntity;
            var shipPosition = GetComponentDataFromEntity<Translation>(true);
            var screenWidth = Screen.width;
            var screenHeight = Screen.height;
            var level = m_LevelGroup.ToComponentDataArrayAsync<LevelComponent>(Allocator.TempJob, out levelHandle);
            var teleport = m_Teleport;

            var trackJob = Entities.WithReadOnly(shipPosition).WithReadOnly(level).WithDisposeOnCompletion(level).
                ForEach((ref LineRendererComponentData target) =>
            {
                int mapWidth = level[0].width;
                int mapHeight = level[0].height;
                int nextTeleport = 1;

                if (shipPosition.HasComponent(localPlayerShip))
                {
                    float3 pos = shipPosition[localPlayerShip].Value;
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

                teleport[0] = nextTeleport;
            }).Schedule(JobHandle.CombineDependencies(Dependency, levelHandle));
            Dependency = trackJob;
        }
    }

    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ShipRenderSystem : SystemBase
    {
        private EntityQuery m_LineGroup;
        private NativeQueue<LineRenderSystem.Line>.ParallelWriter m_LineQueue;

        protected override void OnCreate()
        {
            m_LineGroup = GetEntityQuery(ComponentType.ReadWrite<LineRendererComponentData>());
            m_LineQueue = World.GetOrCreateSystem<LineRenderSystem>().LineQueue;
        }

        override protected void OnUpdate()
        {
            if (m_LineGroup.IsEmptyIgnoreFilter)
                return;

            float shipWidth = 10;
            float shipHeight = 20;
            var shipLineWidth = 2;
            var shipColor = new float4(0.85f, 0.85f, 0.85f, 1);
            var shipTop = new float3(0, shipHeight / 2, 0);
            var shipBL = new float3(-shipWidth / 2, -shipHeight / 2, 0);
            var shipBR = new float3(shipWidth / 2, -shipHeight / 2, 0);
            var lines = m_LineQueue;

            Entities.WithAll<ShipTagComponentData>().ForEach((in Translation position, in Rotation rotation) =>
            {
                float3 pos = position.Value;
                var rot = rotation.Value;

                var rotTop = pos + math.mul(rot, shipTop);
                var rotBL = pos + math.mul(rot, shipBL);
                var rotBR = pos + math.mul(rot, shipBR);
                lines.Enqueue(new LineRenderSystem.Line(rotTop.xy, rotBL.xy, shipColor, shipLineWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotTop.xy, rotBR.xy, shipColor, shipLineWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotBL.xy, rotBR.xy, shipColor, shipLineWidth));
            }).ScheduleParallel();
        }
    }
}
