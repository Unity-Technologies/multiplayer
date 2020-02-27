using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.NetCode;

public struct LagCompensationGhostSerializerCollection : IGhostSerializerCollection
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
    public static int FindGhostType<T>()
        where T : struct, ISnapshotData<T>
    {
        if (typeof(T) == typeof(LagCubeSnapshotData))
            return 0;
        if (typeof(T) == typeof(LagPlayerSnapshotData))
            return 1;
        return -1;
    }

    public void BeginSerialize(ComponentSystemBase system)
    {
        m_LagCubeGhostSerializer.BeginSerialize(system);
        m_LagPlayerGhostSerializer.BeginSerialize(system);
    }

    public int CalculateImportance(int serializer, ArchetypeChunk chunk)
    {
        switch (serializer)
        {
            case 0:
                return m_LagCubeGhostSerializer.CalculateImportance(chunk);
            case 1:
                return m_LagPlayerGhostSerializer.CalculateImportance(chunk);
        }

        throw new ArgumentException("Invalid serializer type");
    }

    public int GetSnapshotSize(int serializer)
    {
        switch (serializer)
        {
            case 0:
                return m_LagCubeGhostSerializer.SnapshotSize;
            case 1:
                return m_LagPlayerGhostSerializer.SnapshotSize;
        }

        throw new ArgumentException("Invalid serializer type");
    }

    public int Serialize(ref DataStreamWriter dataStream, SerializeData data)
    {
        switch (data.ghostType)
        {
            case 0:
            {
                return GhostSendSystem<LagCompensationGhostSerializerCollection>.InvokeSerialize<LagCubeGhostSerializer, LagCubeSnapshotData>(m_LagCubeGhostSerializer, ref dataStream, data);
            }
            case 1:
            {
                return GhostSendSystem<LagCompensationGhostSerializerCollection>.InvokeSerialize<LagPlayerGhostSerializer, LagPlayerSnapshotData>(m_LagPlayerGhostSerializer, ref dataStream, data);
            }
            default:
                throw new ArgumentException("Invalid serializer type");
        }
    }
    private LagCubeGhostSerializer m_LagCubeGhostSerializer;
    private LagPlayerGhostSerializer m_LagPlayerGhostSerializer;
}

public struct EnableLagCompensationGhostSendSystemComponent : IComponentData
{}
public class LagCompensationGhostSendSystem : GhostSendSystem<LagCompensationGhostSerializerCollection>
{
    protected override void OnCreate()
    {
        base.OnCreate();
        RequireSingletonForUpdate<EnableLagCompensationGhostSendSystemComponent>();
    }
}
