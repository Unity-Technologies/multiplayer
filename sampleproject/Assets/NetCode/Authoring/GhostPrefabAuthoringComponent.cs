using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.XR;

public class GhostPrefabAuthoringComponent : MonoBehaviour, IConvertGameObjectToEntity
{
    public GameObject prefab;

    Type FindSnapshot()
    {
        var allTypes = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            IEnumerable<Type> asmTypes = null;
            try
            {
                asmTypes = assembly.GetTypes();

            }
            catch (ReflectionTypeLoadException e)
            {
                asmTypes = e.Types.Where(t => t != null);
                Debug.LogWarning(
                    $"GhostPrefabAuthoringComponent failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
            }

            allTypes.AddRange(asmTypes.Where(t=>t.Name == $"{prefab.name}SnapshotData" && (typeof(IComponentData).IsAssignableFrom(t) || typeof(IBufferElementData).IsAssignableFrom(t))));
        }

        if (allTypes.Count != 1)
        {
            throw new InvalidOperationException("Could not find snapshot data for ghost type, did you generate the ghost code?");
        }

        return allTypes[0];
    }

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var snapshotType = FindSnapshot();
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
        if (World.Active == ClientServerBootstrap.serverWorld)
        {
            var prefabEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(prefab, World.Active);
            dstManager.AddComponentData(entity, new GhostServerPrefabComponent {prefab = prefabEntity});
        }
#endif
#if !UNITY_SERVER
        bool isClientPrefab = false;
        foreach (var clientWorld in ClientServerBootstrap.clientWorld)
            isClientPrefab |= World.Active == clientWorld;
        if (isClientPrefab)
        {
            var ghostAuth = prefab.GetComponent<GhostAuthoringComponent>();
            var origInstantiate = ghostAuth.ClientInstantiateTarget;
            ghostAuth.ClientInstantiateTarget = GhostAuthoringComponent.ClientInstantiateTargetType.Interpolated;
            var prefabEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(prefab, World.Active);
            ghostAuth.ClientInstantiateTarget = GhostAuthoringComponent.ClientInstantiateTargetType.Predicted;
            var predictedPrefabEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(prefab, World.Active);
            ghostAuth.ClientInstantiateTarget = origInstantiate;
            // TODO: should these be part of GhostAuthoringComponent conversion too?
            dstManager.AddComponent(prefabEntity, snapshotType);
            dstManager.AddComponent(predictedPrefabEntity, snapshotType);
            dstManager.AddComponentData(entity, new GhostClientPrefabComponent {predictedPrefab = predictedPrefabEntity, interpolatedPrefab = prefabEntity});
            dstManager.AddComponent(entity, snapshotType);
        }
#endif
#if UNITY_EDITOR || (!UNITY_SERVER && !UNITY_CLIENT)
        if (!isClientPrefab && World.Active != ClientServerBootstrap.serverWorld)
        {
            throw new InvalidOperationException(
                $"A ghost prefab can only be created in the client or server world, not {World.Active.Name}");
        }
#endif
    }
}
