using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Unity.NetCode;
using UnityEngine.Rendering;

namespace Asteroids.Client
{
    [UpdateBefore(typeof(ParticleEmitterSystem))]
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    public partial class ShipThrustParticleSystem : SystemBase
    {
        override protected void OnUpdate()
        {
            Entities.ForEach((ref ParticleEmitterComponentData emitter, in ShipStateComponentData state) =>
            {
                emitter.active = state.State;
            }).ScheduleParallel();
        }
    }

    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public partial class ShipTrackingSystem : SystemBase
    {
        private EntityQuery m_LevelGroup;
        private NativeArray<int> m_Teleport;
        NativeArray<float2> m_RenderOffset;
        Matrix4x4 m_Scale = Matrix4x4.Scale(new Vector3(1, -1, 1));

        void BeginRendering(ScriptableRenderContext ctx, Camera cam)
        {
            cam.ResetProjectionMatrix();
            cam.projectionMatrix = cam.projectionMatrix * m_Scale;
        }

        protected override void OnCreate()
        {
            m_Teleport = new NativeArray<int>(1, Allocator.Persistent);
            m_Teleport[0] = 1;
            m_RenderOffset = new NativeArray<float2>(2, Allocator.Persistent);
            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
        }

        protected override void OnStartRunning()
        {
            RenderPipelineManager.beginCameraRendering += BeginRendering;
        }
        protected override void OnStopRunning()
        {
            RenderPipelineManager.beginCameraRendering -= BeginRendering;
        }

        protected override void OnDestroy()
        {
            m_Teleport.Dispose();
            m_RenderOffset.Dispose();
        }

        override protected void OnUpdate()
        {
            JobHandle levelHandle;
            TryGetSingletonEntity<ShipCommandData>(out var localPlayerShip);
            var shipPosition = GetComponentDataFromEntity<Translation>(true);
            var screenWidth = Screen.width;
            var screenHeight = Screen.height;
            var level = m_LevelGroup.ToComponentDataArrayAsync<LevelComponent>(Allocator.TempJob, out levelHandle);
            var teleport = m_Teleport;

            var renderOffset = m_RenderOffset;
            var curOffset = renderOffset[0];
            Camera.main.orthographicSize = screenHeight / 2;
            Camera.main.transform.position = new Vector3(curOffset.x + Screen.width/2, curOffset.y + Screen.height/2, 0);

            var deltaTime = Time.DeltaTime;

            var trackJob = Job.WithReadOnly(shipPosition).WithReadOnly(level).WithDisposeOnCompletion(level).
                WithCode(() =>
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
                    renderOffset[1] = pos.xy;
                    nextTeleport = 0;
                }

                var offset = renderOffset[0];
                var target = renderOffset[1];
                float maxPxPerSec = 500;
                if (math.any(offset != target))
                {
                    if (teleport[0] != 0)
                        offset = target;
                    else
                    {
                        float2 delta = (target - offset);
                        float deltaLen = math.length(delta);
                        float maxDiff = maxPxPerSec * deltaTime;
                        if (deltaLen > maxDiff || deltaLen < -maxDiff)
                            delta *= maxDiff / deltaLen;
                        offset += delta;
                    }

                    renderOffset[0] = offset;
                }


                teleport[0] = nextTeleport;
            }).Schedule(JobHandle.CombineDependencies(Dependency, levelHandle));
            Dependency = trackJob;
        }
    }
}
