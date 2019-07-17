using Unity.Entities;
using UnityEngine;

public class ParticleEmitterAuthoringComponent : MonoBehaviour, IConvertGameObjectToEntity
{
    public ParticleEmitterComponentData emitter;
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, emitter);
    }
}
