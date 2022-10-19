using UnityEngine;
using Unity.Entities;

public class RayTraceCommandAuthoring : MonoBehaviour
{
}

public class RayTraceCommandAuthoringBaker : Baker<RayTraceCommandAuthoring>
{
    public override void Bake(RayTraceCommandAuthoring authoring)
    {
        AddBuffer<RayTraceCommand>();
        AddComponent(new LagPlayer());
    }
}

