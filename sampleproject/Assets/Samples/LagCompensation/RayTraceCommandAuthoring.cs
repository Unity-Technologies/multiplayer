using UnityEngine;
using Unity.Entities;


public class RayTraceCommandAuthoring : MonoBehaviour, IConvertGameObjectToEntity
{
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddBuffer<RayTraceCommand>(entity);
        dstManager.AddComponentData(entity, new LagPlayer());
    }
}

