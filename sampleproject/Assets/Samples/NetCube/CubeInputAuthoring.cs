using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.NetCode.Samples.Common;
using UnityEngine;

[GhostComponent(PrefabType=GhostPrefabType.AllPredicted)]
public struct CubeInput : IInputComponentData
{
    public int Horizontal;
    public int Vertical;
}

[DisallowMultipleComponent]
public class CubeInputAuthoring : MonoBehaviour
{
    class CubeInputBaking : Unity.Entities.Baker<CubeInputAuthoring>
    {
        public override void Bake(CubeInputAuthoring authoring)
        {
            AddComponent<CubeInput>();
        }
    }
}

[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial struct SampleCubeInput : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkIdComponent>();
        state.RequireForUpdate<NetCubeSpawner>();
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach (var playerInput in SystemAPI.Query<RefRW<CubeInput>>().WithAll<GhostOwnerIsLocal>())
        {
            playerInput.ValueRW = default;
            if (Input.GetKey("left") || TouchInput.GetKey(TouchInput.KeyCode.Left))
                playerInput.ValueRW.Horizontal -= 1;
            if (Input.GetKey("right") || TouchInput.GetKey(TouchInput.KeyCode.Right))
                playerInput.ValueRW.Horizontal += 1;
            if (Input.GetKey("down") || TouchInput.GetKey(TouchInput.KeyCode.Down))
                playerInput.ValueRW.Vertical -= 1;
            if (Input.GetKey("up") || TouchInput.GetKey(TouchInput.KeyCode.Up))
                playerInput.ValueRW.Vertical += 1;
        }
    }
}
