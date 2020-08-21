using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Networking.Transport.Utilities;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateAfter(typeof(GhostSpawnClassificationSystem))]
public class BulletGhostSpawnClassificationSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<GhostSpawnQueueComponent>();
        RequireSingletonForUpdate<PredictedGhostSpawnList>();
    }
    protected override void OnUpdate()
    {
        var spawnListEntity = GetSingletonEntity<PredictedGhostSpawnList>();
        var spawnListFromEntity = GetBufferFromEntity<PredictedGhostSpawn>();
        Dependency = Entities
            .WithAll<GhostSpawnQueueComponent>()
            .WithoutBurst()
            .ForEach((DynamicBuffer<GhostSpawnBuffer> ghosts, DynamicBuffer<SnapshotDataBuffer> data) =>
        {
            var spawnList = spawnListFromEntity[spawnListEntity];
            for (int i = 0; i < ghosts.Length; ++i)
            {
                var ghost = ghosts[i];
                if (ghost.SpawnType == GhostSpawnBuffer.Type.Predicted)
                {
                    for (int j = 0; j < spawnList.Length; ++j)
                    {
                        if (ghost.GhostType == spawnList[j].ghostType && !SequenceHelpers.IsNewer(spawnList[j].spawnTick, ghost.ServerSpawnTick + 5) && SequenceHelpers.IsNewer(spawnList[j].spawnTick + 5, ghost.ServerSpawnTick))
                        {
                            ghost.PredictedSpawnEntity = spawnList[j].entity;
                            spawnList[j] = spawnList[spawnList.Length-1];
                            spawnList.RemoveAt(spawnList.Length - 1);
                            break;
                        }
                    }
                    ghosts[i] = ghost;
                }
            }
        }).Schedule(Dependency);
    }
}
