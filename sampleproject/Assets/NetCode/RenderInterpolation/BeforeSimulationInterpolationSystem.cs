using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(NetworkStreamReceiveSystem))]
public class BeforeSimulationInterpolationSystem : JobComponentSystem
{
    private EntityQuery positionInterpolationGroup;
    private EntityQuery rotationInterpolationGroup;
    protected override void OnCreateManager()
    {
        positionInterpolationGroup = GetEntityQuery(ComponentType.ReadOnly<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<PreviousSimulatedPosition>(), ComponentType.ReadWrite<Translation>());
        rotationInterpolationGroup = GetEntityQuery(ComponentType.ReadOnly<CurrentSimulatedRotation>(),
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
        var topGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
        RenderInterpolationSystem.parameters.startTime = topGroup.UpdateTime;
        RenderInterpolationSystem.parameters.fixedDeltaTime = topGroup.UpdateDeltaTime;

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
