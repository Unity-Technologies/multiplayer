
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

[GhostComponent(PrefabType=GhostPrefabType.AllPredicted)]
[GenerateAuthoringComponent]
public struct CubeInput : ICommandData
{
    public uint Tick {get; set;}
    public int horizontal;
    public int vertical;
}

[UpdateInGroup(typeof(GhostInputSystemGroup))]
[AlwaysSynchronizeSystem]
public partial class SampleCubeInput : SystemBase
{
    ClientSimulationSystemGroup m_ClientSimulationSystemGroup;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<NetCubeSpawner>();
        m_ClientSimulationSystemGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
    }

    protected override void OnUpdate()
    {
        if (!TryGetSingletonEntity<CubeInput>(out var localInput))
            return;
        var input = default(CubeInput);
        input.Tick = m_ClientSimulationSystemGroup.ServerTick;
        if (Input.GetKey("left") || TouchInput.GetKey(TouchInput.KeyCode.Left))
            input.horizontal -= 1;
        if (Input.GetKey("right") || TouchInput.GetKey(TouchInput.KeyCode.Right))
            input.horizontal += 1;
        if (Input.GetKey("down") || TouchInput.GetKey(TouchInput.KeyCode.Down))
            input.vertical -= 1;
        if (Input.GetKey("up") || TouchInput.GetKey(TouchInput.KeyCode.Up))
            input.vertical += 1;
        var inputBuffer = EntityManager.GetBuffer<CubeInput>(localInput);
        inputBuffer.AddCommandData(input);
    }
}
