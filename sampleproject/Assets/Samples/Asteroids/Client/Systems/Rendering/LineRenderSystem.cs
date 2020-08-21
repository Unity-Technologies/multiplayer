using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Unity.Jobs;
using Unity.NetCode;

namespace Asteroids.Client
{
    public struct LineRendererComponentData : IComponentData
    {
        public float2 targetOffset;
        public int teleport;
    }

    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class LineRenderSystem : SystemBase
    {
        public NativeQueue<Line>.ParallelWriter LineQueue => m_ConcurrentLineQueue;
        public struct Line
        {
            public Line(float2 start, float2 end, float4 color, float width)
            {
                this.start = start;
                this.end = end;
                this.color = color;
                this.width = width;
            }

            public float2 start;
            public float2 end;
            public float4 color;
            public float width;
        }

        private EntityQuery m_LineGroup;
        private EntityQuery m_LevelGroup;
        private Entity m_SingletonEntity;

        private NativeList<Line> m_LineList;
        private NativeQueue<Line> m_LineQueue;
        private NativeQueue<Line>.ParallelWriter m_ConcurrentLineQueue;
        private NativeArray<float2> m_RenderOffset;

        // Rendering resources
        Material m_Material;
        ComputeBuffer m_ComputeBuffer;
        CommandBuffer m_CommandBuffer;

        const int MaxLines = 10 * 1024;

        protected override void OnUpdate()
        {
            if (Camera.main == null)
                return;

            var lineList = m_LineList;

            Camera.main.RemoveCommandBuffers(CameraEvent.AfterEverything);
            Camera.main.AddCommandBuffer(CameraEvent.AfterEverything, m_CommandBuffer);
            if (lineList.Length > MaxLines)
            {
                Debug.LogWarning("Trying to render " + lineList.Length + " but limit is " + MaxLines);
                lineList.ResizeUninitialized(MaxLines);
            }

            var curOffset = m_RenderOffset[0];

            NativeArray<Line> lines = lineList;
            m_Material.SetFloat("offsetX", curOffset.x);
            m_Material.SetFloat("offsetY", curOffset.y);
            m_Material.SetFloat("screenWidth", Screen.width);
            m_Material.SetFloat("screenHeight", Screen.height);
            m_Material.SetBuffer("lines", m_ComputeBuffer);
            m_ComputeBuffer.SetData(lines);
            m_CommandBuffer.Clear();
            m_CommandBuffer.DrawProcedural(Matrix4x4.identity, m_Material, -1, MeshTopology.Triangles,
                lineList.Length * 6);
            lineList.Clear();

            JobHandle levelHandle;
            var list = m_LineList;
            var queue = m_LineQueue;
            var renderOffset = m_RenderOffset;
            var renderSize = new float2(Screen.width, Screen.height);
            var deltaTime = Time.DeltaTime;
            var level = m_LevelGroup.ToComponentDataArrayAsync<LevelComponent>(Allocator.TempJob, out levelHandle);

            var copyToListJob = Entities.WithReadOnly(level).WithDisposeOnCompletion(level).ForEach(
                (ref LineRendererComponentData lineData) =>
                {
                    if (level.Length > 0)
                    {
                        list.Add(new Line(new float2(0, 0), new float2(level[0].width, 0), new float4(1, 0, 0, 1), 5));
                        list.Add(new Line(new float2(0, 0), new float2(0, level[0].height), new float4(1, 0, 0, 1), 5));
                        list.Add(new Line(new float2(0, level[0].height), new float2(level[0].width, level[0].height),
                            new float4(1, 0, 0, 1), 5));
                        list.Add(new Line(new float2(level[0].width, 0), new float2(level[0].width, level[0].height),
                            new float4(1, 0, 0, 1), 5));
                    }

                    var offset = renderOffset[0];
                    var target = lineData.targetOffset;
                    renderOffset[1] = target;
                    float maxPxPerSec = 500;
                    if (math.any(offset != target))
                    {
                        if (lineData.teleport != 0)
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

                    Line line;
                    while (queue.TryDequeue(out line))
                    {
                        if ((line.start.x < offset.x - line.width && line.end.x < offset.x - line.width) ||
                            (line.start.x > offset.x + renderSize.x + line.width &&
                             line.end.x > offset.x + renderSize.x + line.width) ||
                            (line.start.y < offset.y - line.width && line.end.y < offset.y - line.width) ||
                            (line.start.y > offset.y + renderSize.y + line.width &&
                             line.end.y > offset.y + renderSize.y + line.width))
                            continue;
                        list.Add(line);
                    }
                }).Schedule(JobHandle.CombineDependencies(Dependency, levelHandle));
            Dependency = copyToListJob;
            m_LineGroup.AddDependency(Dependency);
        }

        protected override void OnCreate()
        {
            var shader = Shader.Find("LineRenderer");
            if (shader == null)
            {
                Debug.Log("Wrong shader");
                m_Material = null;
                return;
            }

            m_Material = new Material(shader);
            m_ComputeBuffer = new ComputeBuffer(MaxLines, UnsafeUtility.SizeOf<Line>());
            m_CommandBuffer = new CommandBuffer();

            m_LineList = new NativeList<Line>(MaxLines, Allocator.Persistent);
            m_LineQueue = new NativeQueue<Line>(Allocator.Persistent);
            m_ConcurrentLineQueue = m_LineQueue.AsParallelWriter();

            // Fake singleton entity
            m_SingletonEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(m_SingletonEntity, new LineRendererComponentData());

            m_Material.SetBuffer("lines", m_ComputeBuffer);
            m_Material.renderQueue = (int) RenderQueue.Transparent;

            m_RenderOffset = new NativeArray<float2>(2, Allocator.Persistent);

            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            m_LineGroup = GetEntityQuery(ComponentType.ReadWrite<LineRendererComponentData>());
        }

        protected override void OnDestroy()
        {
            m_RenderOffset.Dispose();
            EntityManager.DestroyEntity(m_SingletonEntity);
            m_LineList.Dispose();
            m_LineQueue.Dispose();
            m_CommandBuffer.Release();
            m_ComputeBuffer.Release();
        }
    }
}
