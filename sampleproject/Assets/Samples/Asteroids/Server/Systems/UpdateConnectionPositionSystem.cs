using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Transforms;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(GhostSimulationSystemGroup))]
    public class UpdateConnectionPositionSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var translationFromEntity = GetComponentDataFromEntity<Translation>(true);
            Entities.WithReadOnly(translationFromEntity).ForEach(
                (ref GhostConnectionPosition conPos, in CommandTargetComponent target) =>
                {
                    if (!translationFromEntity.HasComponent(target.targetEntity))
                        return;
                    conPos = new GhostConnectionPosition
                    {
                        Position = translationFromEntity[target.targetEntity].Value
                    };
                }).Schedule();
        }
    }
}
