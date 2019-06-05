using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateAfter(typeof(RpcSendSystem))]
[UpdateAfter(typeof(GhostReceiveSystemGroup))]
public class AfterSimulationInterpolationSystem : JobComponentSystem
{
    private BeforeSimulationInterpolationSystem beforeSystem;
    // Commands needs to be applied before next simulation is run
    private BeginSimulationEntityCommandBufferSystem barrier;
    private EntityQuery positionInterpolationGroup;
    private EntityQuery rotationInterpolationGroup;
    private EntityQuery newPositionInterpolationGroup;
    private EntityQuery newRotationInterpolationGroup;
    protected override void OnCreateManager()
    {
        positionInterpolationGroup = GetEntityQuery(ComponentType.ReadOnly<CurrentSimulatedPosition>(),
            ComponentType.ReadOnly<PreviousSimulatedPosition>(), ComponentType.ReadWrite<Translation>());
        rotationInterpolationGroup = GetEntityQuery(ComponentType.ReadOnly<CurrentSimulatedRotation>(),
            ComponentType.ReadOnly<PreviousSimulatedRotation>(), ComponentType.ReadWrite<Rotation>());
        newPositionInterpolationGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {ComponentType.ReadWrite<Translation>(), ComponentType.ReadOnly<CurrentSimulatedPosition>()},
            None = new[] {ComponentType.ReadWrite<PreviousSimulatedPosition>()}
        });
        newRotationInterpolationGroup = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {ComponentType.ReadWrite<Rotation>(), ComponentType.ReadOnly<CurrentSimulatedRotation>()},
            None = new[] {ComponentType.ReadWrite<PreviousSimulatedRotation>()}
        });

        barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        beforeSystem = World.GetOrCreateSystem<BeforeSimulationInterpolationSystem>();
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
