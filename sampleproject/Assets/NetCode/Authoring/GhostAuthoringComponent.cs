using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class GhostAuthoringComponent : MonoBehaviour
{
    [Serializable]
    public struct GhostComponentField
    {
        public string name;
        public int quantization;
        public bool interpolate;
        internal Type dataType;
        public Type DataType
        {
            get { return dataType; }
            set { dataType = value; }
        }
    }

    [Serializable]
    public struct GhostComponent
    {
        public string name;
        public bool interpolatedClient;
        public bool predictedClient;
        public bool server;
        public GhostComponentField[] fields;
        public bool isManual;
    }

    public enum ClientInstantiateTargetType
    {
        Predicted,
        Interpolated
    }

    public ClientInstantiateTargetType ClientInstantiateTarget = ClientInstantiateTargetType.Predicted;
    public string SnapshotDataPath = "SnapshotData.cs";
    public string SpawnSystemPath = "GhostSpawnSystem.cs";
    public string UpdateSystemPath = "GhostUpdateSystem.cs";
    public string SerializerPath = "GhostSerializer.cs";
    public string Importance = "1";
    public GhostComponent[] Components;

    [HideInInspector]
    public bool doNotStrip = false;
}

[UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
class GhostAuthoringConversion : GameObjectConversionSystem
{
    protected override void OnUpdate()
    {

        Entities.ForEach((GhostAuthoringComponent ghostAuthoring) =>
        {
            if (ghostAuthoring.doNotStrip)
                return;
            var entity = GetPrimaryEntity(ghostAuthoring);

            var toRemove = new HashSet<string>();
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
            if (World.Active == ClientServerBootstrap.serverWorld)
            {
                DstEntityManager.AddComponentData(entity, new GhostComponent());
                // Create server version of prefab
                foreach (var comp in ghostAuthoring.Components)
                {
                    if (!comp.server)
                        toRemove.Add(comp.name);
                }
            }
#endif
#if !UNITY_SERVER
            bool isClientPrefab = false;
            foreach (var clientWorld in ClientServerBootstrap.clientWorld)
                isClientPrefab |= World.Active == clientWorld;
            if (isClientPrefab)
            {
                DstEntityManager.AddComponentData(entity, new ReplicatedEntityComponent());
                if (ghostAuthoring.ClientInstantiateTarget == GhostAuthoringComponent.ClientInstantiateTargetType.Interpolated)
                {
                    foreach (var comp in ghostAuthoring.Components)
                    {
                        if (!comp.interpolatedClient)
                            toRemove.Add(comp.name);
                    }
                }
                else
                {
                    DstEntityManager.AddComponentData(entity, new PredictedEntityComponent());
                    foreach (var comp in ghostAuthoring.Components)
                    {
                        if (!comp.predictedClient)
                            toRemove.Add(comp.name);
                    }

                }
            }
#endif
#if UNITY_EDITOR || (!UNITY_SERVER && !UNITY_CLIENT)
            if (!isClientPrefab && World.Active != ClientServerBootstrap.serverWorld)
            {
                throw new InvalidOperationException(
                    $"A ghost prefab can only be created in the client or server world, not {World.Active.Name}");
            }
#endif

            // Add list of things to strip based on target world
            // Strip the things in GhostAuthoringConversion
            var components = DstEntityManager.GetComponentTypes(entity);
            foreach (var comp in components)
            {
                if (toRemove.Contains(comp.GetManagedType().Name))
                    DstEntityManager.RemoveComponent(entity, comp);
            }

        });
    }
}
