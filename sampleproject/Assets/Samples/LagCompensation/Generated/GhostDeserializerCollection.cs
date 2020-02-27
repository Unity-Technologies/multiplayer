using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.NetCode;

public struct LagCompensationGhostDeserializerCollection : IGhostDeserializerCollection
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public string[] CreateSerializerNameList()
    {
        var arr = new string[]
        {
            "LagCubeGhostSerializer",
            "LagPlayerGhostSerializer",
        };
        return arr;
    }

    public int Length => 2;
#endif
    public void Initialize(World world)
    {
        var curLagCubeGhostSpawnSystem = world.GetOrCreateSystem<LagCubeGhostSpawnSystem>();
        m_LagCubeSnapshotDataNewGhostIds = curLagCubeGhostSpawnSystem.NewGhostIds;
        m_LagCubeSnapshotDataNewGhosts = curLagCubeGhostSpawnSystem.NewGhosts;
        curLagCubeGhostSpawnSystem.GhostType = 0;
        var curLagPlayerGhostSpawnSystem = world.GetOrCreateSystem<LagPlayerGhostSpawnSystem>();
        m_LagPlayerSnapshotDataNewGhostIds = curLagPlayerGhostSpawnSystem.NewGhostIds;
        m_LagPlayerSnapshotDataNewGhosts = curLagPlayerGhostSpawnSystem.NewGhosts;
        curLagPlayerGhostSpawnSystem.GhostType = 1;
    }

    public void BeginDeserialize(JobComponentSystem system)
    {
        m_LagCubeSnapshotDataFromEntity = system.GetBufferFromEntity<LagCubeSnapshotData>();
        m_LagPlayerSnapshotDataFromEntity = system.GetBufferFromEntity<LagPlayerSnapshotData>();
    }
    public bool Deserialize(int serializer, Entity entity, uint snapshot, uint baseline, uint baseline2, uint baseline3,
        ref DataStreamReader reader, NetworkCompressionModel compressionModel)
    {
        switch (serializer)
        {
            case 0:
                return GhostReceiveSystem<LagCompensationGhostDeserializerCollection>.InvokeDeserialize(m_LagCubeSnapshotDataFromEntity, entity, snapshot, baseline, baseline2,
                baseline3, ref reader, compressionModel);
            case 1:
                return GhostReceiveSystem<LagCompensationGhostDeserializerCollection>.InvokeDeserialize(m_LagPlayerSnapshotDataFromEntity, entity, snapshot, baseline, baseline2,
                baseline3, ref reader, compressionModel);
            default:
                throw new ArgumentException("Invalid serializer type");
        }
    }
    public void Spawn(int serializer, int ghostId, uint snapshot, ref DataStreamReader reader,
        NetworkCompressionModel compressionModel)
    {
        switch (serializer)
        {
            case 0:
                m_LagCubeSnapshotDataNewGhostIds.Add(ghostId);
                m_LagCubeSnapshotDataNewGhosts.Add(GhostReceiveSystem<LagCompensationGhostDeserializerCollection>.InvokeSpawn<LagCubeSnapshotData>(snapshot, ref reader, compressionModel));
                break;
            case 1:
                m_LagPlayerSnapshotDataNewGhostIds.Add(ghostId);
                m_LagPlayerSnapshotDataNewGhosts.Add(GhostReceiveSystem<LagCompensationGhostDeserializerCollection>.InvokeSpawn<LagPlayerSnapshotData>(snapshot, ref reader, compressionModel));
                break;
            default:
                throw new ArgumentException("Invalid serializer type");
        }
    }

    private BufferFromEntity<LagCubeSnapshotData> m_LagCubeSnapshotDataFromEntity;
    private NativeList<int> m_LagCubeSnapshotDataNewGhostIds;
    private NativeList<LagCubeSnapshotData> m_LagCubeSnapshotDataNewGhosts;
    private BufferFromEntity<LagPlayerSnapshotData> m_LagPlayerSnapshotDataFromEntity;
    private NativeList<int> m_LagPlayerSnapshotDataNewGhostIds;
    private NativeList<LagPlayerSnapshotData> m_LagPlayerSnapshotDataNewGhosts;
}
public struct EnableLagCompensationGhostReceiveSystemComponent : IComponentData
{}
public class LagCompensationGhostReceiveSystem : GhostReceiveSystem<LagCompensationGhostDeserializerCollection>
{
    protected override void OnCreate()
    {
        base.OnCreate();
        RequireSingletonForUpdate<EnableLagCompensationGhostReceiveSystemComponent>();
    }
}
