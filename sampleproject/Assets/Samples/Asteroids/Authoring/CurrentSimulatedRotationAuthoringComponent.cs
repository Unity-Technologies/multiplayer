using Unity.Entities;
using UnityEngine;

public class CurrentSimulatedRotationAuthoringComponent : MonoBehaviour, IConvertGameObjectToEntity
{
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new CurrentSimulatedRotation());
    }
}
