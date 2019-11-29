using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.NetCode;

public struct AsteroidsGhostSerializerCollection : IGhostSerializerCollection
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
    public static int FindGhostType<T>()
        where T : struct, ISnapshotData<T>
    {
        if (typeof(T) == typeof(ShipSnapshotData))
            return 0;
        if (typeof(T) == typeof(AsteroidSnapshotData))
            return 1;
        if (typeof(T) == typeof(BulletSnapshotData))
            return 2;
        return -1;
    }
    public int FindSerializer(EntityArchetype arch)
    {
        if (m_ShipGhostSerializer.CanSerialize(arch))
            return 0;
        if (m_AsteroidGhostSerializer.CanSerialize(arch))
            return 1;
        if (m_BulletGhostSerializer.CanSerialize(arch))
            return 2;
        throw new ArgumentException("Invalid serializer type");
    }

    public void BeginSerialize(ComponentSystemBase system)
    {
        m_ShipGhostSerializer.BeginSerialize(system);
        m_AsteroidGhostSerializer.BeginSerialize(system);
        m_BulletGhostSerializer.BeginSerialize(system);
    }

    public int CalculateImportance(int serializer, ArchetypeChunk chunk)
    {
        switch (serializer)
        {
            case 0:
                return m_ShipGhostSerializer.CalculateImportance(chunk);
            case 1:
                return m_AsteroidGhostSerializer.CalculateImportance(chunk);
            case 2:
                return m_BulletGhostSerializer.CalculateImportance(chunk);
        }

        throw new ArgumentException("Invalid serializer type");
    }

    public bool WantsPredictionDelta(int serializer)
    {
        switch (serializer)
        {
            case 0:
                return m_ShipGhostSerializer.WantsPredictionDelta;
            case 1:
                return m_AsteroidGhostSerializer.WantsPredictionDelta;
            case 2:
                return m_BulletGhostSerializer.WantsPredictionDelta;
        }

        throw new ArgumentException("Invalid serializer type");
    }

    public int GetSnapshotSize(int serializer)
    {
        switch (serializer)
        {
            case 0:
                return m_ShipGhostSerializer.SnapshotSize;
            case 1:
                return m_AsteroidGhostSerializer.SnapshotSize;
            case 2:
                return m_BulletGhostSerializer.SnapshotSize;
        }

        throw new ArgumentException("Invalid serializer type");
    }

    public int Serialize(SerializeData data)
    {
        switch (data.ghostType)
        {
            case 0:
            {
                return GhostSendSystem<AsteroidsGhostSerializerCollection>.InvokeSerialize<ShipGhostSerializer, ShipSnapshotData>(m_ShipGhostSerializer, data);
            }
            case 1:
            {
                return GhostSendSystem<AsteroidsGhostSerializerCollection>.InvokeSerialize<AsteroidGhostSerializer, AsteroidSnapshotData>(m_AsteroidGhostSerializer, data);
            }
            case 2:
            {
                return GhostSendSystem<AsteroidsGhostSerializerCollection>.InvokeSerialize<BulletGhostSerializer, BulletSnapshotData>(m_BulletGhostSerializer, data);
            }
            default:
                throw new ArgumentException("Invalid serializer type");
        }
    }
    private ShipGhostSerializer m_ShipGhostSerializer;
    private AsteroidGhostSerializer m_AsteroidGhostSerializer;
    private BulletGhostSerializer m_BulletGhostSerializer;
}

public struct EnableAsteroidsGhostSendSystemComponent : IComponentData
{}
public class AsteroidsGhostSendSystem : GhostSendSystem<AsteroidsGhostSerializerCollection>
{
    protected override void OnCreate()
    {
        base.OnCreate();
        RequireSingletonForUpdate<EnableAsteroidsGhostSendSystemComponent>();
    }
}
