#if ENABLE_UNITY_COLLECTIONS_CHECKS
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

[DisableAutoCreation]
class GhostStatsCollectionSystem : ComponentSystem
{
    internal string GhostList { get; private set; }

    internal byte[] GhostStatData { get; private set; }
    internal int GhostStatSize { get; private set; }
    internal int GhostStatCount { get; set; }
    const int k_MaxGhostStats = 16;

    internal string GetName()
    {
        var clientSys = World.GetExistingSystem<ClientSimulationSystemGroup>();
        if (clientSys != null)
            return "Client" + clientSys.ClientWorldIndex;
        return "Server";
    }

    public void SetGhostNames(string[] nameList)
    {
        GhostList = "[\"Destroy\"";
        for (int i = 0; i < nameList.Length; ++i)
        {
            GhostList += ",\"" + nameList[i] + "\"";
        }

        GhostList += "]";

        // 3 ints per ghost type, plus one for destroy
        GhostStatSize = (nameList.Length + 1) * 3 * 4 + 4*3;
        GhostStatData = new byte[GhostStatSize*k_MaxGhostStats];
    }

    public unsafe void AddSnapshotReceiveStats(NativeArray<uint> stats)
    {
        if (GhostStatCount >= k_MaxGhostStats)
            return;
        var statBytes = (byte*)stats.GetUnsafeReadOnlyPtr();
        for (int i = 0; i < GhostStatSize-4*3; ++i)
            GhostStatData[GhostStatSize*GhostStatCount + i + 4*3] = statBytes[i];
        ++GhostStatCount;
    }

    public unsafe void SetTargetTime(uint interpolationTick, uint predictionTick)
    {
        fixed (byte* bdata = GhostStatData)
        {
            uint* data = (uint*) bdata;
            data[GhostStatSize / 4 * GhostStatCount + 1] = interpolationTick;
            data[GhostStatSize / 4 * GhostStatCount + 2] = predictionTick;
        }
    }

    protected override void OnUpdate()
    {
    }
}
#endif
