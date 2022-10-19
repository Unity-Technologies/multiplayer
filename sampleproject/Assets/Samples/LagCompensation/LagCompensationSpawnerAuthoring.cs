using Unity.Entities;
using UnityEngine;

public struct LagCompensationSpawner : IComponentData
{
    public Entity prefab;
}

[DisallowMultipleComponent]
public class LagCompensationSpawnerAuthoring : MonoBehaviour
{
    public GameObject prefab;

    class LagCompensationSpawnerBaker : Baker<LagCompensationSpawnerAuthoring>
    {
        public override void Bake(LagCompensationSpawnerAuthoring authoring)
        {
            LagCompensationSpawner component = default(LagCompensationSpawner);
            component.prefab = GetEntity(authoring.prefab);
            AddComponent(component);
        }
    }
}
