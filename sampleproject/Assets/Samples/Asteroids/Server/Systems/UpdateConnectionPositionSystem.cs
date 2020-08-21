using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Transforms;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(GhostSimulationSystemGroup))]
    public class UpdateConnectionPositionSystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var translationFromEntity = GetComponentDataFromEntity<Translation>(true);
            return Entities.WithNativeDisableContainerSafetyRestriction(translationFromEntity).ForEach(
                (ref GhostConnectionPosition conPos, in CommandTargetComponent target) =>
                {
                    if (!translationFromEntity.HasComponent(target.targetEntity))
                        return;
                    conPos = new GhostConnectionPosition
                    {
                        Position = translationFromEntity[target.targetEntity].Value
                    };
                }).Schedule(inputDeps);
        }
    }
}
