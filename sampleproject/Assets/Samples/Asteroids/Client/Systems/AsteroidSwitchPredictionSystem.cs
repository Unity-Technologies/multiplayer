using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    public partial class AsteroidSwitchPredictionSystem : SystemBase
    {
        private NativeList<Entity> m_ToPredicted;
        private NativeList<Entity> m_ToInterpolated;
        private GhostSpawnSystem m_GhostSpawnSystem;
        protected override void OnCreate()
        {
            RequireSingletonForUpdate<ClientSettings>();
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
                if (EntityManager.HasComponent<GhostComponent>(toPredicted[i]))
                    spawnSystem.ConvertGhostToPredicted(toPredicted[i], 1.0f);
            }
            for (int i = 0; i < toInterpolated.Length; ++i)
            {
                if (EntityManager.HasComponent<GhostComponent>(toInterpolated[i]))
                    spawnSystem.ConvertGhostToInterpolated(toInterpolated[i], 1.0f);
            }
            toPredicted.Clear();
            toInterpolated.Clear();

            var settings = GetSingleton<ClientSettings>();
            if (settings.predictionRadius <= 0)
                return;

            if (!TryGetSingletonEntity<ShipCommandData>(out var playerEnt) || !EntityManager.HasComponent<Translation>(playerEnt))
                return;
            var playerPos = EntityManager.GetComponentData<Translation>(playerEnt).Value;

            var radiusSq = settings.predictionRadius*settings.predictionRadius;
            Entities
                .WithNone<StaticAsteroid>()
                .WithNone<PredictedGhostComponent>()
                .WithAll<AsteroidTagComponentData>()
                .ForEach((Entity ent, in Translation position) =>
            {
                if (math.distancesq(playerPos, position.Value) < radiusSq)
                {
                    // convert to predicted
                    toPredicted.Add(ent);
                }
            }).Schedule();
            radiusSq = settings.predictionRadius + settings.predictionRadiusMargin;
            radiusSq = radiusSq*radiusSq;
            Entities
                .WithNone<StaticAsteroid>()
                .WithAll<PredictedGhostComponent>()
                .WithAll<AsteroidTagComponentData>()
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
}
