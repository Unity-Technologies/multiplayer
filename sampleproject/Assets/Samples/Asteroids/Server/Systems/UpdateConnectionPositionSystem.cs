using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Transforms;

namespace Asteroids.Server
{
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct UpdateConnectionPositionSystem : ISystem
    {
        ComponentLookup<Translation> m_Translations;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_Translations = state.GetComponentLookup<Translation>(true);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_Translations.Update(ref state);
            var updateJob = new UpdateConnectionPositionSystemJob
            {
                translationFromEntity = m_Translations
            };
            updateJob.Schedule();
        }

        [BurstCompile]
        partial struct UpdateConnectionPositionSystemJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<Translation> translationFromEntity;

            public void Execute(ref GhostConnectionPosition conPos, in CommandTargetComponent target)
            {
                if (!translationFromEntity.HasComponent(target.targetEntity))
                    return;
                conPos = new GhostConnectionPosition
                {
                    Position = translationFromEntity[target.targetEntity].Value
                };
            }
        }
    }
}
