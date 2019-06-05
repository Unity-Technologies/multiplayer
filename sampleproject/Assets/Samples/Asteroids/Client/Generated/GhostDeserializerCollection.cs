using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
public struct GhostDeserializerCollection : IGhostDeserializerCollection
{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    public string[] CreateSerializerNameList()
    {
        var arr = new string[]
        {
            "ShipGhostSerializer",
            "AsteroidGhostSerializer",
            "BulletGhostSerializer",

        };
        return arr;
    }

    public int Length => 3;
#endif
    public void Initialize(World world)
    {
        var curShipGhostSpawnSystem = world.GetOrCreateSystem<ShipGhostSpawnSystem>();
        m_ShipSnapshotDataNewGhostIds = curShipGhostSpawnSystem.NewGhostIds;
        m_ShipSnapshotDataNewGhosts = curShipGhostSpawnSystem.NewGhosts;
        curShipGhostSpawnSystem.GhostType = 0;
        var curAsteroidGhostSpawnSystem = world.GetOrCreateSystem<AsteroidGhostSpawnSystem>();
        m_AsteroidSnapshotDataNewGhostIds = curAsteroidGhostSpawnSystem.NewGhostIds;
        m_AsteroidSnapshotDataNewGhosts = curAsteroidGhostSpawnSystem.NewGhosts;
        curAsteroidGhostSpawnSystem.GhostType = 1;
        var curBulletGhostSpawnSystem = world.GetOrCreateSystem<BulletGhostSpawnSystem>();
        m_BulletSnapshotDataNewGhostIds = curBulletGhostSpawnSystem.NewGhostIds;
        m_BulletSnapshotDataNewGhosts = curBulletGhostSpawnSystem.NewGhosts;
        curBulletGhostSpawnSystem.GhostType = 2;

    }

    public void BeginDeserialize(JobComponentSystem system)
    {
        m_ShipSnapshotDataFromEntity = system.GetBufferFromEntity<ShipSnapshotData>();
        m_AsteroidSnapshotDataFromEntity = system.GetBufferFromEntity<AsteroidSnapshotData>();
        m_BulletSnapshotDataFromEntity = system.GetBufferFromEntity<BulletSnapshotData>();

    }
    public void Deserialize(int serializer, Entity entity, uint snapshot, uint baseline, uint baseline2, uint baseline3,
        DataStreamReader reader,
        ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
    {
        switch (serializer)
        {
        case 0:
            GhostReceiveSystem<GhostDeserializerCollection>.InvokeDeserialize(m_ShipSnapshotDataFromEntity, entity, snapshot, baseline, baseline2,
                baseline3, reader, ref ctx, compressionModel);
            break;
        case 1:
            GhostReceiveSystem<GhostDeserializerCollection>.InvokeDeserialize(m_AsteroidSnapshotDataFromEntity, entity, snapshot, baseline, baseline2,
                baseline3, reader, ref ctx, compressionModel);
            break;
        case 2:
            GhostReceiveSystem<GhostDeserializerCollection>.InvokeDeserialize(m_BulletSnapshotDataFromEntity, entity, snapshot, baseline, baseline2,
                baseline3, reader, ref ctx, compressionModel);
            break;

        default:
            throw new ArgumentException("Invalid serializer type");
        }
    }
    public void Spawn(int serializer, int ghostId, uint snapshot, DataStreamReader reader,
        ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
    {
        switch (serializer)
        {
            case 0:
                m_ShipSnapshotDataNewGhostIds.Add(ghostId);
                m_ShipSnapshotDataNewGhosts.Add(GhostReceiveSystem<GhostDeserializerCollection>.InvokeSpawn<ShipSnapshotData>(snapshot, reader, ref ctx, compressionModel));
                break;
            case 1:
                m_AsteroidSnapshotDataNewGhostIds.Add(ghostId);
                m_AsteroidSnapshotDataNewGhosts.Add(GhostReceiveSystem<GhostDeserializerCollection>.InvokeSpawn<AsteroidSnapshotData>(snapshot, reader, ref ctx, compressionModel));
                break;
            case 2:
                m_BulletSnapshotDataNewGhostIds.Add(ghostId);
                m_BulletSnapshotDataNewGhosts.Add(GhostReceiveSystem<GhostDeserializerCollection>.InvokeSpawn<BulletSnapshotData>(snapshot, reader, ref ctx, compressionModel));
                break;

            default:
                throw new ArgumentException("Invalid serializer type");
        }
    }

    private BufferFromEntity<ShipSnapshotData> m_ShipSnapshotDataFromEntity;
    private NativeList<int> m_ShipSnapshotDataNewGhostIds;
    private NativeList<ShipSnapshotData> m_ShipSnapshotDataNewGhosts;
    private BufferFromEntity<AsteroidSnapshotData> m_AsteroidSnapshotDataFromEntity;
    private NativeList<int> m_AsteroidSnapshotDataNewGhostIds;
    private NativeList<AsteroidSnapshotData> m_AsteroidSnapshotDataNewGhosts;
    private BufferFromEntity<BulletSnapshotData> m_BulletSnapshotDataFromEntity;
    private NativeList<int> m_BulletSnapshotDataNewGhostIds;
    private NativeList<BulletSnapshotData> m_BulletSnapshotDataNewGhosts;

}
public class MultiplayerSampleGhostReceiveSystem : GhostReceiveSystem<GhostDeserializerCollection>
{
}
