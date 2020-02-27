using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;

public partial class BulletGhostSpawnSystem
{
    protected override JobHandle MarkPredictedGhosts(NativeArray<BulletSnapshotData> snapshots, NativeArray<int> predictedMask, NativeList<PredictSpawnGhost> predictionSpawnGhosts, JobHandle inputDeps)
    {
        JobHandle playerHandle;
        var job = new MarkPredictedJob
        {
            snapshot = snapshots,
            predictedMask = predictedMask,
            predictionSpawnGhosts = predictionSpawnGhosts,
            playerIds = m_PlayerGroup.ToComponentDataArrayAsync<NetworkIdComponent>(Allocator.TempJob, out playerHandle),
        };
        return job.Schedule(predictedMask.Length, 8, JobHandle.CombineDependencies(inputDeps, playerHandle));
    }

    [BurstCompile]
    struct MarkPredictedJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BulletSnapshotData> snapshot;
        public NativeArray<int> predictedMask;
        [ReadOnly] public NativeList<PredictSpawnGhost> predictionSpawnGhosts;
        [ReadOnly][DeallocateOnJobCompletion] public NativeArray<NetworkIdComponent> playerIds;
        public void Execute(int i)
        {
            bool selfSpawn = playerIds.Length > 0 && (snapshot[i].GetPlayerIdComponentDataPlayerId() == playerIds[0].Value);
            if (selfSpawn)
            {
                predictedMask[i] = 1;
                // Check if this entity has already been spawned if the spawn time for this entity
                for (int ent = 0; ent < predictionSpawnGhosts.Length; ++ent)
                {
                    // If this new ghost spawn is approximately on the same tick as a predicted spawn we did earlier then
                    // it's the same entity (tick is equal or slightly larger) and we should create a ghost ID mapping
                    // to the predict spawned entity (so snapshot updates will find it)
                    if (snapshot[i].Tick >= predictionSpawnGhosts[ent].snapshotData.Tick &&
                        snapshot[i].Tick - predictionSpawnGhosts[ent].snapshotData.Tick <= 10)
                    {
                        // race condition if multiple jobs try to bind to the same entity, handled by underlaying system but would be nice to avoid here
                        predictedMask[i] = ent + 2;
                        break;
                    }
                }
            }
        }
    }

    [BurstCompile]
    struct CalculateVelocityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> entities;
        [NativeDisableParallelForRestriction] public BufferFromEntity<BulletSnapshotData> snapshotFromEntity;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<Velocity> velocityFromEntity;
        public float bulletVelocity;
        public void Execute(int i)
        {
            var entity = entities[i];

            var snapshot = snapshotFromEntity[entity];
            var snapshotData = snapshot[0];
            velocityFromEntity[entity] = new Velocity { Value = math.mul(snapshotData.GetRotationValue(), new float3(0, bulletVelocity, 0)).xy };
        }

    }
    protected override JobHandle UpdateNewPredictedEntities(NativeArray<Entity> entities, JobHandle inputDeps)
    {
        var job = new CalculateVelocityJob
        {
            entities = entities,
            snapshotFromEntity = GetBufferFromEntity<BulletSnapshotData>(),
            velocityFromEntity = GetComponentDataFromEntity<Velocity>(),
            bulletVelocity = GetSingleton<LevelComponent>().bulletVelocity
        };
        return job.Schedule(entities.Length, 8, inputDeps);
    }
}
