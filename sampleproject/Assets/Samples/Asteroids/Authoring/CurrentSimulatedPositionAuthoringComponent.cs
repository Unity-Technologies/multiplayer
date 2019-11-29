using Unity.Entities;
using UnityEngine;
using Unity.NetCode;

public class CurrentSimulatedPositionAuthoringComponent : MonoBehaviour, IConvertGameObjectToEntity
{
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new CurrentSimulatedPosition());
    }
}
