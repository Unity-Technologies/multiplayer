using Unity.Entities;

[GenerateAuthoringComponent]
public struct PredictionSwitchingSpawner : IComponentData
{
    public Entity Player;
}
