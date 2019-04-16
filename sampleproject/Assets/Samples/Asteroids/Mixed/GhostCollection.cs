using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

public struct GhostSerializerCollection
{
    public int FindSerializer(EntityArchetype arch)
    {
        if (shipSerializer.CanSerialize(arch))
            return (int) SerializerType.Ship;
        if (asteroidSerializer.CanSerialize(arch))
            return (int) SerializerType.Asteroid;
        if (bulletSerializer.CanSerialize(arch))
            return (int) SerializerType.Bullet;
        throw new ArgumentException("Invalid serializer type");
    }

    public void BeginSerialize(ComponentSystemBase system)
    {
        shipSerializer.BeginSerialize(system);
        asteroidSerializer.BeginSerialize(system);
        bulletSerializer.BeginSerialize(system);
    }

    public int CalculateImportance(int serializer, ArchetypeChunk chunk)
    {
        switch ((SerializerType) serializer)
        {
            case SerializerType.Ship:
                return shipSerializer.CalculateImportance(chunk);
            case SerializerType.Asteroid:
                return asteroidSerializer.CalculateImportance(chunk);
            case SerializerType.Bullet:
                return bulletSerializer.CalculateImportance(chunk);
        }

        throw new ArgumentException("Invalid serializer type");
    }

    public bool WantsPredictionDelta(int serializer)
    {
        switch ((SerializerType) serializer)
        {
            case SerializerType.Ship:
                return shipSerializer.WantsPredictionDelta;
            case SerializerType.Asteroid:
                return asteroidSerializer.WantsPredictionDelta;
            case SerializerType.Bullet:
                return bulletSerializer.WantsPredictionDelta;
        }

        throw new ArgumentException("Invalid serializer type");
    }

    public int GetSnapshotSize(int serializer)
    {
        switch ((SerializerType) serializer)
        {
            case SerializerType.Ship:
                return shipSerializer.SnapshotSize;
            case SerializerType.Asteroid:
                return asteroidSerializer.SnapshotSize;
            case SerializerType.Bullet:
                return bulletSerializer.SnapshotSize;
        }

        throw new ArgumentException("Invalid serializer type");
    }

    public unsafe int Serialize(int serializer, ArchetypeChunk chunk, int startIndex, uint currentTick,
        Entity* currentSnapshotEntity, void* currentSnapshotData,
        GhostSendSystem.GhostSystemStateComponent* ghosts, NativeArray<Entity> ghostEntities,
        NativeArray<int> baselinePerEntity, NativeList<GhostSendSystem.SnapshotBaseline> availableBaselines,
        DataStreamWriter dataStream, NetworkCompressionModel compressionModel)
    {
        switch ((SerializerType) serializer)
        {
            case SerializerType.Ship:
            {
                return GhostSendSystem.InvokeSerialize<ShipGhostSerializer, ShipSnapshotData>(shipSerializer, serializer,
                    chunk, startIndex, currentTick, currentSnapshotEntity, (ShipSnapshotData*)currentSnapshotData,
                    ghosts, ghostEntities, baselinePerEntity, availableBaselines,
                    dataStream, compressionModel);
            }
            case SerializerType.Asteroid:
            {
                return GhostSendSystem.InvokeSerialize<AsteroidGhostSerializer, AsteroidSnapshotData>(asteroidSerializer, serializer,
                    chunk, startIndex, currentTick, currentSnapshotEntity, (AsteroidSnapshotData*)currentSnapshotData,
                    ghosts, ghostEntities, baselinePerEntity, availableBaselines,
                    dataStream, compressionModel);
            }
            case SerializerType.Bullet:
            {
                return GhostSendSystem.InvokeSerialize<BulletGhostSerializer, BulletSnapshotData>(bulletSerializer, serializer,
                    chunk, startIndex, currentTick, currentSnapshotEntity, (BulletSnapshotData*)currentSnapshotData,
                    ghosts, ghostEntities, baselinePerEntity, availableBaselines,
                    dataStream, compressionModel);
            }
            default:
                throw new ArgumentException("Invalid serializer type");
        }
    }
    internal enum SerializerType
    {
        Ship = 0,
        Asteroid = 1,
        Bullet = 2
    }
    private ShipGhostSerializer shipSerializer;
    private AsteroidGhostSerializer asteroidSerializer;
    private BulletGhostSerializer bulletSerializer;
}

public struct GhostDeserializerCollection
{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    public static string[] CreateSerializerNameList()
    {
        var arr = new string[3];
        arr[0] = typeof(ShipGhostSerializer).Name;
        arr[1] = typeof(AsteroidGhostSerializer).Name;
        arr[2] = typeof(BulletGhostSerializer).Name;
        return arr;
    }

    public int Length => 3;
#endif
    public void Initialize(World world)
    {
        var shipSpawner = world.GetOrCreateManager<ShipGhostSpawnSystem>();
        var asteroidSpawner = world.GetOrCreateManager<AsteroidGhostSpawnSystem>();
        var bulletSpawner = world.GetOrCreateManager<BulletGhostSpawnSystem>();
        shipNewGhostIds = shipSpawner.NewGhostIds;
        shipNewGhosts = shipSpawner.NewGhosts;
        shipSpawner.GhostType = (int) GhostSerializerCollection.SerializerType.Ship;
        asteroidNewGhostIds = asteroidSpawner.NewGhostIds;
        asteroidNewGhosts = asteroidSpawner.NewGhosts;
        asteroidSpawner.GhostType = (int) GhostSerializerCollection.SerializerType.Asteroid;
        bulletNewGhostIds = bulletSpawner.NewGhostIds;
        bulletNewGhosts = bulletSpawner.NewGhosts;
        bulletSpawner.GhostType = (int) GhostSerializerCollection.SerializerType.Bullet;
    }

    public void BeginDeserialize(JobComponentSystem system)
    {
        shipSnapshotFromEntity = system.GetBufferFromEntity<ShipSnapshotData>();
        asteroidSnapshotFromEntity = system.GetBufferFromEntity<AsteroidSnapshotData>();
        bulletSnapshotFromEntity = system.GetBufferFromEntity<BulletSnapshotData>();
    }
    public void Deserialize(int serializer, Entity entity, uint snapshot, uint baseline, uint baseline2, uint baseline3,
        DataStreamReader reader,
        ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
    {
        switch ((GhostSerializerCollection.SerializerType)serializer)
        {
        case GhostSerializerCollection.SerializerType.Ship:
            GhostReceiveSystem.InvokeDeserialize(shipSnapshotFromEntity, entity, snapshot, baseline, baseline2,
                baseline3, reader, ref ctx, compressionModel);
            break;
        case GhostSerializerCollection.SerializerType.Asteroid:
            GhostReceiveSystem.InvokeDeserialize(asteroidSnapshotFromEntity, entity, snapshot, baseline, baseline2,
                baseline3, reader, ref ctx, compressionModel);
            break;
        case GhostSerializerCollection.SerializerType.Bullet:
            GhostReceiveSystem.InvokeDeserialize(bulletSnapshotFromEntity, entity, snapshot, baseline, baseline2,
                baseline3, reader, ref ctx, compressionModel);
            break;
        default:
            throw new ArgumentException("Invalid serializer type");
        }
    }
    public void Spawn(int serializer, int ghostId, uint snapshot, DataStreamReader reader,
        ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
    {
        switch ((GhostSerializerCollection.SerializerType)serializer)
        {
            case GhostSerializerCollection.SerializerType.Ship:
                shipNewGhostIds.Add(ghostId);
                shipNewGhosts.Add(GhostReceiveSystem.InvokeSpawn<ShipSnapshotData>(snapshot, reader, ref ctx, compressionModel));
                break;
            case GhostSerializerCollection.SerializerType.Asteroid:
                asteroidNewGhostIds.Add(ghostId);
                asteroidNewGhosts.Add(GhostReceiveSystem.InvokeSpawn<AsteroidSnapshotData>(snapshot, reader, ref ctx, compressionModel));
                break;
            case GhostSerializerCollection.SerializerType.Bullet:
                bulletNewGhostIds.Add(ghostId);
                bulletNewGhosts.Add(GhostReceiveSystem.InvokeSpawn<BulletSnapshotData>(snapshot, reader, ref ctx, compressionModel));
                break;
            default:
                throw new ArgumentException("Invalid serializer type");
        }
    }

    private BufferFromEntity<ShipSnapshotData> shipSnapshotFromEntity;
    private BufferFromEntity<AsteroidSnapshotData> asteroidSnapshotFromEntity;
    private BufferFromEntity<BulletSnapshotData> bulletSnapshotFromEntity;

    private NativeList<int> shipNewGhostIds;
    private NativeList<ShipSnapshotData> shipNewGhosts;
    private NativeList<int> asteroidNewGhostIds;
    private NativeList<AsteroidSnapshotData> asteroidNewGhosts;
    private NativeList<int> bulletNewGhostIds;
    private NativeList<BulletSnapshotData> bulletNewGhosts;
}

