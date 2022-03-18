using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;
using Unity.Rendering;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public partial class PredictionSwitchingSystem : SystemBase
{
    private NativeList<Entity> m_ToPredicted;
    private NativeList<Entity> m_ToInterpolated;
    private GhostSpawnSystem m_GhostSpawnSystem;
    private float4 m_InterpolatedColor;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<PredictionSwitchingSpawner>();
        RequireSingletonForUpdate<CommandTargetComponent>();
        m_ToPredicted = new NativeList<Entity>(16, Allocator.Persistent);
        m_ToInterpolated = new NativeList<Entity>(16, Allocator.Persistent);
        m_GhostSpawnSystem = World.GetExistingSystem<GhostSpawnSystem>();
    }
    protected override void OnDestroy()
    {
        m_ToPredicted.Dispose();
        m_ToInterpolated.Dispose();
    }
    protected override void OnUpdate()
    {
        var spawnSystem = m_GhostSpawnSystem;
        var toPredicted = m_ToPredicted;
        var toInterpolated = m_ToInterpolated;
        for (int i = 0; i < toPredicted.Length; ++i)
        {
            if (EntityManager.HasComponent<GhostComponent>(toPredicted[i]) &&
                EntityManager.GetComponentData<GhostComponent>(toPredicted[i]).ghostType >= 0)
            {
                spawnSystem.ConvertGhostToPredicted(toPredicted[i], 1.0f);
                if (EntityManager.HasComponent<URPMaterialPropertyBaseColor>(toPredicted[i]))
                {
                    m_InterpolatedColor = EntityManager.GetComponentData<URPMaterialPropertyBaseColor>(toPredicted[i]).Value;
                    EntityManager.SetComponentData(toPredicted[i], new URPMaterialPropertyBaseColor{Value = new float4(0,1,0,1)});
                }
            }
        }
        for (int i = 0; i < toInterpolated.Length; ++i)
        {
            if (EntityManager.HasComponent<GhostComponent>(toInterpolated[i]) &&
                EntityManager.GetComponentData<GhostComponent>(toInterpolated[i]).ghostType >= 0)
            {
                spawnSystem.ConvertGhostToInterpolated(toInterpolated[i], 1.0f);
                if (EntityManager.HasComponent<URPMaterialPropertyBaseColor>(toInterpolated[i]))
                    EntityManager.SetComponentData(toInterpolated[i], new URPMaterialPropertyBaseColor{Value = m_InterpolatedColor});
            }
        }
        toPredicted.Clear();
        toInterpolated.Clear();

        var playerEnt = GetSingleton<CommandTargetComponent>().targetEntity;
        if (playerEnt == Entity.Null)
            return;
        var playerPos = EntityManager.GetComponentData<Translation>(playerEnt).Value;

        float radius = 5;
        // The margin must be large enough that moving from predicted time to interpolated time does not move the ghost back into the prediction sphere
        float margin = 2.5f;
        float radiusSq = radius*radius;
        Entities
            .WithNone<PredictedGhostComponent>()
            .WithAll<GhostComponent>()
            .ForEach((Entity ent, in Translation position) =>
        {
            if (math.distancesq(playerPos, position.Value) < radiusSq)
            {
                // convert to predicted
                toPredicted.Add(ent);
            }
        }).Schedule();
        radiusSq = radius + margin;
        radiusSq = radiusSq*radiusSq;
        Entities
            .WithAll<PredictedGhostComponent>()
            .WithAll<GhostComponent>()
            .ForEach((Entity ent, in Translation position) =>
        {
            if (math.distancesq(playerPos, position.Value) > radiusSq)
            {
                // convert to interpolated
                toInterpolated.Add(ent);
            }
        }).Schedule();
    }
}
