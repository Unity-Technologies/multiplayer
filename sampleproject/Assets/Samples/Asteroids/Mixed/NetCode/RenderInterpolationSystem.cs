using Asteroids.Client;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public struct RenderInterpolationParameters
{
    public float startTime;
    public float fixedDeltaTime;
}

[UpdateInGroup(typeof(ClientPresentationSystemGroup))]
public class RenderInterpolationSystem : JobComponentSystem
{
    // FIXME: should use singleton component
    public static RenderInterpolationParameters parameters;
    private ComponentGroup posInterpolationGroup;
    private ComponentGroup rotInterpolationGroup;
    private uint lastInterpolationVersion;
    protected override void OnCreateManager()
    {
        posInterpolationGroup = GetComponentGroup(
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadOnly<CurrentSimulatedPosition>(),
            ComponentType.ReadOnly<PreviousSimulatedPosition>());
        rotInterpolationGroup = GetComponentGroup(
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadOnly<CurrentSimulatedRotation>(),
            ComponentType.ReadOnly<PreviousSimulatedRotation>());
    }

    [BurstCompile]
    struct PosInterpolateJob : IJobChunk
    {
        public float curWeight;
        public float prevWeight;
        public ArchetypeChunkComponentType<Translation> positionType;
        [ReadOnly] public ArchetypeChunkComponentType<CurrentSimulatedPosition> curPositionType;
        [ReadOnly] public ArchetypeChunkComponentType<PreviousSimulatedPosition> prevPositionType;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            // If current was written after copying it to prev we need to interpolate, otherwise they must be identical
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(curPositionType), chunk.GetComponentVersion(prevPositionType)))
            {
                var prevPos = chunk.GetNativeArray(prevPositionType);
                var curPos = chunk.GetNativeArray(curPositionType);
                var pos = chunk.GetNativeArray(positionType);
                for (var ent = 0; ent < pos.Length; ++ent)
                {
                    var p = curPos[ent].Value * curWeight + prevPos[ent].Value * prevWeight;
                    pos[ent] = new Translation {Value = p};
                }
            }
        }
    }
    [BurstCompile]
    struct RotInterpolateJob : IJobChunk
    {
        public float curWeight;
        public float prevWeight;
        public ArchetypeChunkComponentType<Rotation> rotationType;
        [ReadOnly] public ArchetypeChunkComponentType<CurrentSimulatedRotation> curRotationType;
        [ReadOnly] public ArchetypeChunkComponentType<PreviousSimulatedRotation> prevRotationType;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            // If current was written after copying it to prev we need to interpolate, otherwise they must be identical
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(curRotationType), chunk.GetComponentVersion(prevRotationType)))
            {
                var prevRot = chunk.GetNativeArray(prevRotationType);
                var curRot = chunk.GetNativeArray(curRotationType);
                var rot = chunk.GetNativeArray(rotationType);
                for (var ent = 0; ent < rot.Length; ++ent)
                {
                    var a = math.slerp(prevRot[ent].Value, curRot[ent].Value, curWeight);
                    rot[ent] = new Rotation {Value = a};
                }
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var posInterpolateJob = new PosInterpolateJob();
        var rotInterpolateJob = new RotInterpolateJob();
        posInterpolateJob.positionType = GetArchetypeChunkComponentType<Translation>();
        posInterpolateJob.prevPositionType = GetArchetypeChunkComponentType<PreviousSimulatedPosition>(true);
        posInterpolateJob.curPositionType = GetArchetypeChunkComponentType<CurrentSimulatedPosition>(true);
        rotInterpolateJob.rotationType = GetArchetypeChunkComponentType<Rotation>();
        rotInterpolateJob.prevRotationType = GetArchetypeChunkComponentType<PreviousSimulatedRotation>(true);
        rotInterpolateJob.curRotationType = GetArchetypeChunkComponentType<CurrentSimulatedRotation>(true);

        posInterpolateJob.curWeight = rotInterpolateJob.curWeight = (Time.time - parameters.startTime) / parameters.fixedDeltaTime;
        posInterpolateJob.prevWeight = rotInterpolateJob.prevWeight = 1.0f - posInterpolateJob.curWeight;

        lastInterpolationVersion = GlobalSystemVersion;

        JobHandle dep = posInterpolateJob.Schedule(posInterpolationGroup, inputDeps);
        return rotInterpolateJob.Schedule(rotInterpolationGroup, dep);
    }
}
////////// Simulation //////////
[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(NetworkStreamReceiveSystem))]
public class BeforeSimulationInterpolationSystem : JobComponentSystem
{
    private ComponentGroup positionInterpolationGroup;
    private ComponentGroup rotationInterpolationGroup;
    protected override void OnCreateManager()
    {
        positionInterpolationGroup = GetComponentGroup(ComponentType.ReadOnly<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<PreviousSimulatedPosition>(), ComponentType.ReadWrite<Translation>());
        rotationInterpolationGroup = GetComponentGroup(ComponentType.ReadOnly<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<PreviousSimulatedRotation>(), ComponentType.ReadWrite<Rotation>());
    }

    public uint simEndComponentVersion;
    public uint simStartComponentVersion;

    [BurstCompile]
    struct UpdatePos : IJobChunk
    {
        public ArchetypeChunkComponentType<Translation> positionType;
        [ReadOnly] public ArchetypeChunkComponentType<CurrentSimulatedPosition> curPositionType;
        public ArchetypeChunkComponentType<PreviousSimulatedPosition> prevPositionType;
        public uint simStartComponentVersion;
        public uint simEndComponentVersion;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            // For all chunks where currentTrans is newer than previousTrans
            // Copy currentTrans to previous trans
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(curPositionType), simStartComponentVersion))
            {
                var curPos = chunk.GetNativeArray(curPositionType);
                var prevPos = chunk.GetNativeArray(prevPositionType);
                // FIXME: use a memcopy since size and layout must be identical
                for (int ent = 0; ent < curPos.Length; ++ent)
                {
                    prevPos[ent] = new PreviousSimulatedPosition {Value = curPos[ent].Value};
                }
            }

            // For all chunks where transform has changed since end of last simulation
            // Copy currentTargs to trans
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(positionType), simEndComponentVersion))
            {
                // Transform was interpolated by the rendering system
                var curPos = chunk.GetNativeArray(curPositionType);
                var pos = chunk.GetNativeArray(positionType);
                // FIXME: use a memcopy since size and layout must be identical
                for (int ent = 0; ent < curPos.Length; ++ent)
                {
                    pos[ent] = new Translation {Value = curPos[ent].Value};
                }
            }
        }
    }
    [BurstCompile]
    struct UpdateRot : IJobChunk
    {
        public ArchetypeChunkComponentType<Rotation> rotationType;
        [ReadOnly] public ArchetypeChunkComponentType<CurrentSimulatedRotation> curRotationType;
        public ArchetypeChunkComponentType<PreviousSimulatedRotation> prevRotationType;
        public uint simStartComponentVersion;
        public uint simEndComponentVersion;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            // For all chunks where currentTrans is newer than previousTrans
            // Copy currentTrans to previous trans
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(curRotationType), simStartComponentVersion))
            {
                var curRot = chunk.GetNativeArray(curRotationType);
                var prevRot = chunk.GetNativeArray(prevRotationType);
                // FIXME: use a memcopy since size and layout must be identical
                for (int ent = 0; ent < curRot.Length; ++ent)
                {
                    prevRot[ent] = new PreviousSimulatedRotation {Value = curRot[ent].Value};
                }
            }

            // For all chunks where transform has changed since end of last simulation
            // Copy currentTargs to trans
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(rotationType), simEndComponentVersion))
            {
                // Transform was interpolated by the rendering system
                var curRot = chunk.GetNativeArray(curRotationType);
                var rot = chunk.GetNativeArray(rotationType);
                // FIXME: use a memcopy since size and layout must be identical
                for (int ent = 0; ent < curRot.Length; ++ent)
                {
                    rot[ent] = new Rotation {Value = curRot[ent].Value};
                }
            }
        }
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        RenderInterpolationSystem.parameters.startTime = Time.fixedTime;
        RenderInterpolationSystem.parameters.fixedDeltaTime = Time.fixedDeltaTime;

        var posJob = new UpdatePos();
        posJob.positionType = GetArchetypeChunkComponentType<Translation>();
        posJob.curPositionType = GetArchetypeChunkComponentType<CurrentSimulatedPosition>(true);
        posJob.prevPositionType = GetArchetypeChunkComponentType<PreviousSimulatedPosition>();
        posJob.simStartComponentVersion = simStartComponentVersion;
        posJob.simEndComponentVersion = simEndComponentVersion;

        var rotJob = new UpdateRot();
        rotJob.rotationType = GetArchetypeChunkComponentType<Rotation>();
        rotJob.curRotationType = GetArchetypeChunkComponentType<CurrentSimulatedRotation>(true);
        rotJob.prevRotationType = GetArchetypeChunkComponentType<PreviousSimulatedRotation>();
        rotJob.simStartComponentVersion = simStartComponentVersion;
        rotJob.simEndComponentVersion = simEndComponentVersion;

        var handles = new NativeArray<JobHandle>(2, Allocator.Temp);
        handles[0] = posJob.Schedule(positionInterpolationGroup, inputDeps);
        handles[1] = rotJob.Schedule(rotationInterpolationGroup, inputDeps);

        simStartComponentVersion = GlobalSystemVersion;

        return JobHandle.CombineDependencies(handles);
    }
}

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateAfter(typeof(NetworkStreamSendSystem))]
[UpdateAfter(typeof(GhostReceiveSystemGroup))]
public class AfterSimulationInterpolationSystem : JobComponentSystem
{
    private BeforeSimulationInterpolationSystem beforeSystem;
    // Commands needs to be applied before next simulation is run
    private BeginSimulationEntityCommandBufferSystem barrier;
    private ComponentGroup positionInterpolationGroup;
    private ComponentGroup rotationInterpolationGroup;
    private ComponentGroup newPositionInterpolationGroup;
    private ComponentGroup newRotationInterpolationGroup;
    protected override void OnCreateManager()
    {
        positionInterpolationGroup = GetComponentGroup(ComponentType.ReadOnly<CurrentSimulatedPosition>(),
            ComponentType.ReadOnly<PreviousSimulatedPosition>(), ComponentType.ReadWrite<Translation>());
        rotationInterpolationGroup = GetComponentGroup(ComponentType.ReadOnly<CurrentSimulatedRotation>(),
            ComponentType.ReadOnly<PreviousSimulatedRotation>(), ComponentType.ReadWrite<Rotation>());
        newPositionInterpolationGroup = GetComponentGroup(new EntityArchetypeQuery
        {
            All = new[] {ComponentType.ReadWrite<Translation>(), ComponentType.ReadOnly<CurrentSimulatedPosition>()},
            None = new[] {ComponentType.ReadWrite<PreviousSimulatedPosition>()}
        });
        newRotationInterpolationGroup = GetComponentGroup(new EntityArchetypeQuery
        {
            All = new[] {ComponentType.ReadWrite<Rotation>(), ComponentType.ReadOnly<CurrentSimulatedRotation>()},
            None = new[] {ComponentType.ReadWrite<PreviousSimulatedRotation>()}
        });

        barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        beforeSystem = World.GetOrCreateManager<BeforeSimulationInterpolationSystem>();
    }

    [BurstCompile]
    struct UpdateCurrentPosJob : IJobChunk
    {
        [ReadOnly] public ArchetypeChunkComponentType<Translation> positionType;
        public ArchetypeChunkComponentType<CurrentSimulatedPosition> curPositionType;
        public uint simStartComponentVersion;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            // For all chunks where trans has changed since start of simulation
            // Copy trans to currentTrans
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(positionType), simStartComponentVersion))
            {
                // Transform was interpolated by the rendering system
                var curPos = chunk.GetNativeArray(curPositionType);
                var pos = chunk.GetNativeArray(positionType);
                // FIXME: use a memcopy since size and layout must be identical
                for (int ent = 0; ent < curPos.Length; ++ent)
                {
                    curPos[ent] = new CurrentSimulatedPosition {Value = pos[ent].Value};
                }
            }
        }
    }
    [BurstCompile]
    struct UpdateCurrentRotJob : IJobChunk
    {
        [ReadOnly] public ArchetypeChunkComponentType<Rotation> rotationType;
        public ArchetypeChunkComponentType<CurrentSimulatedRotation> curRotationType;
        public uint simStartComponentVersion;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            // For all chunks where trans has changed since start of simulation
            // Copy trans to currentTrans
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(rotationType), simStartComponentVersion))
            {
                // Transform was interpolated by the rendering system
                var curRot = chunk.GetNativeArray(curRotationType);
                var rot = chunk.GetNativeArray(rotationType);
                // FIXME: use a memcopy since size and layout must be identical
                for (int ent = 0; ent < curRot.Length; ++ent)
                {
                    curRot[ent] = new CurrentSimulatedRotation {Value = rot[ent].Value};
                }
            }
        }
    }
    struct InitCurrentPosJob : IJobChunk
    {
        [ReadOnly] public ArchetypeChunkEntityType entityType;
        [ReadOnly] public ArchetypeChunkComponentType<Translation> positionType;
        public ArchetypeChunkComponentType<CurrentSimulatedPosition> curPositionType;
        public EntityCommandBuffer.Concurrent commandBuffer;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var curPos = chunk.GetNativeArray(curPositionType);
            var pos = chunk.GetNativeArray(positionType);
            var entity = chunk.GetNativeArray(entityType);
            // FIXME: use a memcopy since size and layout must be identical
            for (int ent = 0; ent < curPos.Length; ++ent)
            {
                var cp = pos[ent];
                curPos[ent] = new CurrentSimulatedPosition {Value = cp.Value};
                commandBuffer.AddComponent(chunkIndex, entity[ent], new PreviousSimulatedPosition {Value = cp.Value});
            }
        }
    }
    struct InitCurrentRotJob : IJobChunk
    {
        [ReadOnly] public ArchetypeChunkEntityType entityType;
        [ReadOnly] public ArchetypeChunkComponentType<Rotation> rotationType;
        public ArchetypeChunkComponentType<CurrentSimulatedRotation> curRotationType;
        public EntityCommandBuffer.Concurrent commandBuffer;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var curRot = chunk.GetNativeArray(curRotationType);
            var rot = chunk.GetNativeArray(rotationType);
            var entity = chunk.GetNativeArray(entityType);
            // FIXME: use a memcopy since size and layout must be identical
            for (int ent = 0; ent < curRot.Length; ++ent)
            {
                var cr = rot[ent];
                curRot[ent] = new CurrentSimulatedRotation {Value = cr.Value};
                commandBuffer.AddComponent(chunkIndex, entity[ent], new PreviousSimulatedRotation {Value = cr.Value});
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var handles = new NativeArray<JobHandle>(2, Allocator.Temp);
        // FIXME: TempJob does not work with really low sim tick frequency
        var curPosJob = new UpdateCurrentPosJob();
        curPosJob.positionType = GetArchetypeChunkComponentType<Translation>(true);
        curPosJob.curPositionType = GetArchetypeChunkComponentType<CurrentSimulatedPosition>();
        curPosJob.simStartComponentVersion = beforeSystem.simStartComponentVersion;
        handles[0] = curPosJob.Schedule(positionInterpolationGroup, inputDeps);

        var curRotJob = new UpdateCurrentRotJob();
        curRotJob.rotationType = GetArchetypeChunkComponentType<Rotation>(true);
        curRotJob.curRotationType = GetArchetypeChunkComponentType<CurrentSimulatedRotation>();
        curRotJob.simStartComponentVersion = beforeSystem.simStartComponentVersion;
        handles[1] = curRotJob.Schedule(rotationInterpolationGroup, inputDeps);

        var initPosJob = new InitCurrentPosJob();
        initPosJob.positionType = curPosJob.positionType;
        initPosJob.curPositionType = curPosJob.curPositionType;
        initPosJob.entityType = GetArchetypeChunkEntityType();

        var initRotJob = new InitCurrentRotJob();
        initRotJob.rotationType = curRotJob.rotationType;
        initRotJob.curRotationType = curRotJob.curRotationType;
        initRotJob.entityType = initPosJob.entityType;

        if (!newPositionInterpolationGroup.IsEmptyIgnoreFilter || !newRotationInterpolationGroup.IsEmptyIgnoreFilter)
        {
            initPosJob.commandBuffer = barrier.CreateCommandBuffer().ToConcurrent();
            initRotJob.commandBuffer = barrier.CreateCommandBuffer().ToConcurrent();
            handles[0] = initPosJob.Schedule(newPositionInterpolationGroup, handles[0]);
            handles[1] = initRotJob.Schedule(newRotationInterpolationGroup, handles[1]);
        }

        beforeSystem.simEndComponentVersion = GlobalSystemVersion;

        var handle = JobHandle.CombineDependencies(handles);
        barrier.AddJobHandleForProducer(handle);
        return handle;
    }
}
