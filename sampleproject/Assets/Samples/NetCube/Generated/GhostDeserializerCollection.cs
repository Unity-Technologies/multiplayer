using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.NetCode;

public struct NetCubeGhostDeserializerCollection : IGhostDeserializerCollection
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public string[] CreateSerializerNameList()
    {
        var arr = new string[]
        {
            "CubeGhostSerializer",
        };
        return arr;
    }

    public int Length => 1;
#endif
    public void Initialize(World world)
    {
        var curCubeGhostSpawnSystem = world.GetOrCreateSystem<CubeGhostSpawnSystem>();
        m_CubeSnapshotDataNewGhostIds = curCubeGhostSpawnSystem.NewGhostIds;
        m_CubeSnapshotDataNewGhosts = curCubeGhostSpawnSystem.NewGhosts;
        curCubeGhostSpawnSystem.GhostType = 0;
    }

    public void BeginDeserialize(JobComponentSystem system)
    {
        m_CubeSnapshotDataFromEntity = system.GetBufferFromEntity<CubeSnapshotData>();
    }
    public bool Deserialize(int serializer, Entity entity, uint snapshot, uint baseline, uint baseline2, uint baseline3,
        ref DataStreamReader reader, NetworkCompressionModel compressionModel)
    {
        switch (serializer)
        {
            case 0:
                return GhostReceiveSystem<NetCubeGhostDeserializerCollection>.InvokeDeserialize(m_CubeSnapshotDataFromEntity, entity, snapshot, baseline, baseline2,
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
                m_CubeSnapshotDataNewGhostIds.Add(ghostId);
                m_CubeSnapshotDataNewGhosts.Add(GhostReceiveSystem<NetCubeGhostDeserializerCollection>.InvokeSpawn<CubeSnapshotData>(snapshot, ref reader, compressionModel));
                break;
            default:
                throw new ArgumentException("Invalid serializer type");
        }
    }

    private BufferFromEntity<CubeSnapshotData> m_CubeSnapshotDataFromEntity;
    private NativeList<int> m_CubeSnapshotDataNewGhostIds;
    private NativeList<CubeSnapshotData> m_CubeSnapshotDataNewGhosts;
}
public struct EnableNetCubeGhostReceiveSystemComponent : IComponentData
{}
public class NetCubeGhostReceiveSystem : GhostReceiveSystem<NetCubeGhostDeserializerCollection>
{
    protected override void OnCreate()
    {
        base.OnCreate();
        RequireSingletonForUpdate<EnableNetCubeGhostReceiveSystemComponent>();
    }
}
