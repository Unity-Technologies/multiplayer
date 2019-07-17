using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GhostAuthoringComponent))]
public class GhostAuthoringComponentEditor : Editor
{
    SerializedProperty ClientInstantiateTarget;
    SerializedProperty SnapshotDataPath;
    SerializedProperty SpawnSystemPath;
    SerializedProperty UpdateSystemPath;
    SerializedProperty SerializerPath;
    SerializedProperty Importance;
    SerializedProperty ghostComponents;

    public static Dictionary<string, GhostAuthoringComponent.GhostComponent> GhostComponentDefaults;
    static GhostAuthoringComponentEditor()
    {
        GhostComponentDefaults = new Dictionary<string, GhostAuthoringComponent.GhostComponent>();
        var comp = new GhostAuthoringComponent.GhostComponent
        {
            name = "Translation",
            server = true,
            interpolatedClient = true,
            predictedClient = true,
            isManual = false,
            fields = new GhostAuthoringComponent.GhostComponentField[1]
        };
        comp.fields[0] = new GhostAuthoringComponent.GhostComponentField
        {
            name = "Value",
            interpolate = true,
            quantization = 10
        };
        GhostComponentDefaults.Add(comp.name, comp);
        comp = new GhostAuthoringComponent.GhostComponent
        {
            name = "Rotation",
            server = true,
            interpolatedClient = true,
            predictedClient = true,
            isManual = false,
            fields = new GhostAuthoringComponent.GhostComponentField[1]
        };
        comp.fields[0] = new GhostAuthoringComponent.GhostComponentField
        {
            name = "Value",
            interpolate = true,
            quantization = 1000
        };
        GhostComponentDefaults.Add(comp.name, comp);
        comp = new GhostAuthoringComponent.GhostComponent
        {
            name = "CurrentSimulatedPosition",
            server = false,
            interpolatedClient = true,
            predictedClient = true,
            isManual = false,
            fields = new GhostAuthoringComponent.GhostComponentField[0]
        };
        GhostComponentDefaults.Add(comp.name, comp);
        comp = new GhostAuthoringComponent.GhostComponent
        {
            name = "CurrentSimulatedRotation",
            server = false,
            interpolatedClient = true,
            predictedClient = true,
            isManual = false,
            fields = new GhostAuthoringComponent.GhostComponentField[0]
        };
        GhostComponentDefaults.Add(comp.name, comp);
    }

    void OnEnable()
    {
        ClientInstantiateTarget = serializedObject.FindProperty("ClientInstantiateTarget");
        SnapshotDataPath = serializedObject.FindProperty("SnapshotDataPath");
        SpawnSystemPath = serializedObject.FindProperty("SpawnSystemPath");
        UpdateSystemPath = serializedObject.FindProperty("UpdateSystemPath");
        SerializerPath = serializedObject.FindProperty("SerializerPath");
        Importance = serializedObject.FindProperty("Importance");
        ghostComponents = serializedObject.FindProperty("Components");
    }

    bool ShowField(SerializedProperty field)
    {
        EditorGUILayout.PropertyField(field.FindPropertyRelative("name"));
        ++EditorGUI.indentLevel;
        var keep = !GUILayout.Button("Delete Component Field");
        EditorGUILayout.PropertyField(field.FindPropertyRelative("quantization"));
        EditorGUILayout.PropertyField(field.FindPropertyRelative("interpolate"));
        --EditorGUI.indentLevel;
        return keep;
    }
    bool ShowComponent(SerializedProperty comp)
    {
        bool keep = true;
        var fields = comp.FindPropertyRelative("fields");
        var fieldName = comp.FindPropertyRelative("name");
        var interpolatedClient = comp.FindPropertyRelative("interpolatedClient");
        var predictedClient = comp.FindPropertyRelative("predictedClient");
        var server = comp.FindPropertyRelative("server");
        var isManual = comp.FindPropertyRelative("isManual").boolValue;
        comp.isExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(comp.isExpanded,
            System.String.Format("{4}{0} ({1}/{2}/{3})",
                fieldName.stringValue, server.boolValue?"S":"-",
                interpolatedClient.boolValue?"IC":"-",
                predictedClient.boolValue?"PC":"-",
                isManual ? "* " : ""));
        if (comp.isExpanded)
        {
            ++EditorGUI.indentLevel;
            if (isManual)
            {
                EditorGUILayout.PropertyField(fieldName);
                keep = !GUILayout.Button("Delete Ghost Component");
            }

            EditorGUILayout.PropertyField(server);
            EditorGUILayout.PropertyField(interpolatedClient);
            EditorGUILayout.PropertyField(predictedClient);
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Fields");
            int removeIdx = -1;
            for (int fi = 0; fi < fields.arraySize; ++fi)
            {
                if (!ShowField(fields.GetArrayElementAtIndex(fi)))
                    removeIdx = fi;
            }
            if (removeIdx >= 0)
                fields.DeleteArrayElementAtIndex(removeIdx);
            if (GUILayout.Button("Add Component Field"))
            {
                fields.InsertArrayElementAtIndex(fields.arraySize);
                var field = fields.GetArrayElementAtIndex(fields.arraySize - 1);
                field.FindPropertyRelative("name").stringValue = "";
                field.FindPropertyRelative("quantization").intValue = 1;
                field.FindPropertyRelative("interpolate").boolValue = false;
            }
            --EditorGUI.indentLevel;
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        return keep;
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(ClientInstantiateTarget);

        EditorGUILayout.PropertyField(SnapshotDataPath);
        //if (GUILayout.Button("Reset to default"))
        //    SnapshotDataPath.stringValue = String.Format("{0}SnapshotData.cs", target.name);
        EditorGUILayout.PropertyField(SpawnSystemPath);
        //if (GUILayout.Button("Reset to default"))
        //    SpawnSystemPath.stringValue = String.Format("{0}GhostSpawnSystem.cs", target.name);
        EditorGUILayout.PropertyField(UpdateSystemPath);
        //if (GUILayout.Button("Reset to default"))
        //    UpdateSystemPath.stringValue = String.Format("{0}GhostUpdateSystem.cs", target.name);
        EditorGUILayout.PropertyField(SerializerPath);
        //if (GUILayout.Button("Reset to default"))
        //    SerializerPath.stringValue = String.Format("{0}GhostSerializer.cs", target.name);
        EditorGUILayout.PropertyField(Importance);

        int removeIdx = -1;
        for (int ci = 0; ci < ghostComponents.arraySize; ++ci)
        {
            if (!ShowComponent(ghostComponents.GetArrayElementAtIndex(ci)))
                removeIdx = ci;
        }

        if (removeIdx >= 0)
        {
            ghostComponents.DeleteArrayElementAtIndex(removeIdx);
        }

#if false
        // Disable manual adding of components for now
        EditorGUILayout.Separator();
        if (GUILayout.Button("Add Ghost Component"))
        {
            ghostComponents.InsertArrayElementAtIndex(ghostComponents.arraySize);
            var comp = ghostComponents.GetArrayElementAtIndex(ghostComponents.arraySize - 1);
            comp.FindPropertyRelative("name").stringValue = "";
            comp.FindPropertyRelative("interpolatedClient").boolValue = true;
            comp.FindPropertyRelative("predictedClient").boolValue = true;
            comp.FindPropertyRelative("server").boolValue = true;
            var fields = comp.FindPropertyRelative("fields");
            fields.arraySize = 0;
            comp.FindPropertyRelative("isManual").boolValue = true;
        }
#endif
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Separator();
        if (GUILayout.Button("Update component list"))
        {
            SyncComponentList();
        }
        if (GUILayout.Button("Generate code"))
        {
            GenerateGhost(target as GhostAuthoringComponent);
        }
    }

    struct ComponentNameComparer: IComparer<ComponentType>
    {
        public int Compare(ComponentType x, ComponentType y) => x.GetManagedType().Name.CompareTo(y.GetManagedType().Name);
    }

    public void SyncComponentList()
    {
        var tempWorld = new World("TempGhostConversion");
        var self = target as GhostAuthoringComponent;
        self.doNotStrip = true;
        var convertedEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(self.gameObject, tempWorld);
        self.doNotStrip = false;

        // Build list of existing components
        var toDelete = new Dictionary<string, int>();
        for (int i = 0; i < self.Components.Length; ++i)
        {
            toDelete.Add(self.Components[i].name, i);
        }

        var newComponents = new List<GhostAuthoringComponent.GhostComponent>();

        var compTypes = tempWorld.EntityManager.GetComponentTypes(convertedEntity);
        compTypes.Sort(default(ComponentNameComparer));
        for (int i = 0; i < compTypes.Length; ++i)
        {
            var managedType = compTypes[i].GetManagedType();
            // TODO: deal with linked entity groups
            if (managedType == typeof(Prefab) || managedType == typeof(LinkedEntityGroup))
                continue;
            if (toDelete.TryGetValue(managedType.Name, out var compIdx))
            {
                toDelete.Remove(managedType.Name);
                newComponents.Add(self.Components[compIdx]);
            }
            else if (GhostComponentDefaults.TryGetValue(managedType.Name, out var defaultData))
            {
                defaultData.fields = (GhostAuthoringComponent.GhostComponentField[])defaultData.fields.Clone();
                newComponents.Add(defaultData);
            }
            else
            {
                // TODO: look up values based on parameters
                newComponents.Add(new GhostAuthoringComponent.GhostComponent
                {
                    name = managedType.Name,
                    interpolatedClient = true,
                    predictedClient = true,
                    server = true,
                    fields = new GhostAuthoringComponent.GhostComponentField[0],
                    isManual = false
                });
            }
        }
        for (int i = 0; i < self.Components.Length; ++i)
        {
            if (self.Components[i].isManual && toDelete.ContainsKey(self.Components[i].name))
                newComponents.Add(self.Components[i]);
        }

        self.Components = newComponents.ToArray();

        tempWorld.Dispose();
    }

    private string assetPath;
    void GenerateGhost(GhostAuthoringComponent ghostInfo)
    {
        List<Type> allTypes = new List<Type>();
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
                    $"GhostImporter failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
            }

            allTypes.AddRange(asmTypes.Where(t=>typeof(IComponentData).IsAssignableFrom(t) || typeof(IBufferElementData).IsAssignableFrom(t) || typeof(ISharedComponentData).IsAssignableFrom(t)));
        }

        // Update type of all fields
        for (int comp = 0; comp < ghostInfo.Components.Length; ++comp)
        {

            var componentTypes = allTypes.Where(t => t.Name == ghostInfo.Components[comp].name);
            if (componentTypes.Count() != 1)
            {
                Debug.LogError("Could not find the type or found several candidates: " + ghostInfo.Components[comp].name);
                return;
            }
            Type componentType = componentTypes.First();
            for (int field = 0; field < ghostInfo.Components[comp].fields.Length; ++field)
            {
                var fieldInfo = componentType.GetField(ghostInfo.Components[comp].fields[field].name);
                if (fieldInfo == null)
                {
                    Debug.LogError("Could not find field: " + ghostInfo.Components[comp].fields[field].name + " in componentType: " + ghostInfo.Components[comp].name);
                    return;
                }
                ghostInfo.Components[comp].fields[field].DataType = fieldInfo.FieldType;
            }
        }

        assetPath = AssetDatabase.GetAssetPath(target);
        if (assetPath == "")
            assetPath = "Assets";
        else
            assetPath = Path.GetDirectoryName(assetPath);

        GenerateSnapshotData(ghostInfo);
        GenerateSerializer(ghostInfo);
        GenerateSpawnSystem(ghostInfo);
        GenerateUpdateSystem(ghostInfo);
    }

    private const string k_SnapshotDataTemplate = @"using Unity.Mathematics;
using Unity.Networking.Transport;

public struct $(GHOSTNAME)SnapshotData : ISnapshotData<$(GHOSTNAME)SnapshotData>
{
    public uint tick;
$(GHOSTFIELDS)

    public uint Tick => tick;
$(GHOSTFIELDSGETSET)

    public void PredictDelta(uint tick, ref $(GHOSTNAME)SnapshotData baseline1, ref $(GHOSTNAME)SnapshotData baseline2)
    {
        var predictor = new GhostDeltaPredictor(tick, this.tick, baseline1.tick, baseline2.tick);
$(GHOSTPREDICT)
    }

    public void Serialize(ref $(GHOSTNAME)SnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
    {
$(GHOSTWRITE)
    }

    public void Deserialize(uint tick, ref $(GHOSTNAME)SnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx,
        NetworkCompressionModel compressionModel)
    {
        this.tick = tick;
$(GHOSTREAD)
    }
    public void Interpolate(ref $(GHOSTNAME)SnapshotData target, float factor)
    {
$(GHOSTINTERPOLATE)
    }
}";

    void GenerateSnapshotData(GhostAuthoringComponent ghostInfo)
    {
        var fields = "";
        var fieldsGetSet = "";
        var predict = "";
        var read = "";
        var write = "";
        var interpolate = "";
        var typeProviders = new List<GhostSnapshotValue>();
        typeProviders.AddRange(GhostSnapshotValue.GameSpecificTypes);

        typeProviders.Add(new GhostSnapshotValueQuaternion());
        typeProviders.Add(new GhostSnapshotValueFloat());
        typeProviders.Add(new GhostSnapshotValueFloat2());
        typeProviders.Add(new GhostSnapshotValueFloat3());
        typeProviders.Add(new GhostSnapshotValueFloat4());
        typeProviders.Add(new GhostSnapshotValueInt());
        typeProviders.Add(new GhostSnapshotValueUInt());
        for (int comp = 0; comp < ghostInfo.Components.Length; ++comp)
        {
            for (int field = 0; field < ghostInfo.Components[comp].fields.Length; ++field)
            {
                bool processed = false;
                foreach (var value in typeProviders)
                {
                    if (value.CanProcess(ghostInfo.Components[comp].fields[field].DataType,
                        ghostInfo.Components[comp].name,
                        ghostInfo.Components[comp].fields[field].name))
                    {
                        fields += value.GenerateMembers(ghostInfo.Components[comp].name,
                            ghostInfo.Components[comp].fields[field].name);
                        fieldsGetSet += value.GenerateGetSet(ghostInfo.Components[comp].name,
                            ghostInfo.Components[comp].fields[field].name,
                            ghostInfo.Components[comp].fields[field].quantization);
                        predict += value.GeneratePredict(ghostInfo.Components[comp].name,
                            ghostInfo.Components[comp].fields[field].name);
                        read += value.GenerateRead(ghostInfo.Components[comp].name,
                            ghostInfo.Components[comp].fields[field].name);
                        write += value.GenerateWrite(ghostInfo.Components[comp].name,
                            ghostInfo.Components[comp].fields[field].name);
                        if (ghostInfo.Components[comp].fields[field].interpolate)
                        {
                            interpolate += value.GenerateInterpolate(ghostInfo.Components[comp].name,
                                ghostInfo.Components[comp].fields[field].name);
                        }

                        processed = true;
                        break;
                    }
                }
                if (!processed)
                {
                    Debug.LogError("Unhandled type " + ghostInfo.Components[comp].fields[field].DataType);
                }
            }
        }

        var snapshotData = k_SnapshotDataTemplate
            .Replace("$(GHOSTNAME)", target.name)
            .Replace("$(GHOSTFIELDS)", fields)
            .Replace("$(GHOSTFIELDSGETSET)", fieldsGetSet)
            .Replace("$(GHOSTPREDICT)", predict)
            .Replace("$(GHOSTREAD)", read)
            .Replace("$(GHOSTWRITE)", write)
            .Replace("$(GHOSTINTERPOLATE)", interpolate);


        File.WriteAllText(Path.Combine(assetPath, ghostInfo.SnapshotDataPath), snapshotData);
    }

    private const string k_SerializerTemplate = @"using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Transforms;

public struct $(GHOSTNAME)GhostSerializer : IGhostSerializer<$(GHOSTNAME)SnapshotData>
{
    // FIXME: These disable safety since all serializers have an instance of the same type - causing aliasing. Should be fixed in a cleaner way
$(GHOSTCOMPONENTTYPES)

    public int CalculateImportance(ArchetypeChunk chunk)
    {
        return $(GHOSTIMPORTANCE);
    }

    public bool WantsPredictionDelta => true;

    public int SnapshotSize => UnsafeUtility.SizeOf<$(GHOSTNAME)SnapshotData>();
    public void BeginSerialize(ComponentSystemBase system)
    {
$(ASSIGNGHOSTCOMPONENTTYPES)
    }

    public bool CanSerialize(EntityArchetype arch)
    {
        var components = arch.GetComponentTypes();
        int matches = 0;
        for (int i = 0; i < components.Length; ++i)
        {
$(GHOSTCOMPONENTCHECK)
        }
        return (matches == $(GHOSTCOMPONENTCOUNT));
    }

    public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref $(GHOSTNAME)SnapshotData snapshot)
    {
        snapshot.tick = tick;
$(GHOSTAPPLY)
    }
}
";

    private const string k_SerializerComponentTypeTemplate =
        @"    private ComponentType componentType$(GHOSTCOMPONENTTYPE);
";
    private const string k_SerializerComponentTypeDataTemplate =
        @"    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<$(GHOSTCOMPONENTTYPE)> ghost$(GHOSTCOMPONENTTYPE)Type;
";
    private const string k_SerializerAssignComponentTypeTemplate =
        @"        componentType$(GHOSTCOMPONENTTYPE) = ComponentType.ReadWrite<$(GHOSTCOMPONENTTYPE)>();
";
    private const string k_SerializerAssignComponentTypeDataTemplate =
        @"        ghost$(GHOSTCOMPONENTTYPE)Type = system.GetArchetypeChunkComponentType<$(GHOSTCOMPONENTTYPE)>();
";
    private const string k_SerializerComponentTypeCheckTemplate =
        @"            if (components[i] == componentType$(GHOSTCOMPONENTTYPE))
                ++matches;
";
    private const string k_SerializerAssignChunkArrayTemplate =
        @"        var chunkData$(GHOSTCOMPONENTTYPE) = chunk.GetNativeArray(ghost$(GHOSTCOMPONENTTYPE)Type);
";
    private const string k_SerializerAssignSnapshotTemplate =
        @"        snapshot.Set$(GHOSTCOMPONENTTYPE)$(GHOSTFIELDNAME)(chunkData$(GHOSTCOMPONENTTYPE)[ent].$(GHOSTFIELDNAME));
";

    void GenerateSerializer(GhostAuthoringComponent ghostInfo)
    {
        string ghostComponentType = "";
        string assignGhostComponentType = "";
        string ghostComponentTypeCheck = "";
        string ghostApply = "";
        int serverComponentCount = 0;
        for (int comp = 0; comp < ghostInfo.Components.Length; ++comp)
        {
            if (!ghostInfo.Components[comp].server)
                continue;
            ++serverComponentCount;
            ghostComponentTypeCheck +=
                k_SerializerComponentTypeCheckTemplate.Replace("$(GHOSTCOMPONENTTYPE)", ghostInfo.Components[comp].name);
            ghostComponentType +=
                k_SerializerComponentTypeTemplate.Replace("$(GHOSTCOMPONENTTYPE)", ghostInfo.Components[comp].name);
            assignGhostComponentType +=
                k_SerializerAssignComponentTypeTemplate.Replace("$(GHOSTCOMPONENTTYPE)", ghostInfo.Components[comp].name);
            if (ghostInfo.Components[comp].fields.Length > 0)
            {
                ghostComponentType +=
                    k_SerializerComponentTypeDataTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        ghostInfo.Components[comp].name);
                assignGhostComponentType +=
                    k_SerializerAssignComponentTypeDataTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        ghostInfo.Components[comp].name);
                ghostApply +=
                    k_SerializerAssignChunkArrayTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        ghostInfo.Components[comp].name);
                for (int field = 0; field < ghostInfo.Components[comp].fields.Length; ++field)
                {
                    ghostApply +=
                        k_SerializerAssignSnapshotTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                                ghostInfo.Components[comp].name)
                            .Replace("$(GHOSTFIELDNAME)",
                                ghostInfo.Components[comp].fields[field].name);
                }
            }
        }

        var serializerData = k_SerializerTemplate
            .Replace("$(GHOSTNAME)", target.name)
            .Replace("$(GHOSTIMPORTANCE)", ghostInfo.Importance)
            .Replace("$(GHOSTCOMPONENTCHECK)", ghostComponentTypeCheck)
            .Replace("$(GHOSTAPPLY)", ghostApply)
            .Replace("$(GHOSTCOMPONENTCOUNT)", serverComponentCount.ToString())
            .Replace("$(ASSIGNGHOSTCOMPONENTTYPES)", assignGhostComponentType)
            .Replace("$(GHOSTCOMPONENTTYPES)", ghostComponentType);
        File.WriteAllText(Path.Combine(assetPath, ghostInfo.SerializerPath), serializerData);
    }

    private const string k_GhostSpawnSystemTemplate =
        @"using Unity.Entities;
using Unity.Transforms;

public partial class $(GHOSTNAME)GhostSpawnSystem : DefaultGhostSpawnSystem<$(GHOSTNAME)SnapshotData>
{
    protected override EntityArchetype GetGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<$(GHOSTNAME)SnapshotData>(),
$(GHOSTINTERPOLATEDCOMPONENTS)
            ComponentType.ReadWrite<ReplicatedEntityComponent>()
        );
    }
    protected override EntityArchetype GetPredictedGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<$(GHOSTNAME)SnapshotData>(),
$(GHOSTPREDICTEDCOMPONENTS)
            ComponentType.ReadWrite<ReplicatedEntityComponent>(),
            ComponentType.ReadWrite<PredictedEntityComponent>()
        );
    }
}
";

    private const string k_GhostSpawnComponentTemplate = @"            ComponentType.ReadWrite<$(GHOSTCOMPONENTTYPE)>(),
";
    void GenerateSpawnSystem(GhostAuthoringComponent ghostInfo)
    {
        string ghostInterpolateComponents = "";
        string ghostPredictComponents = "";
        for (int comp = 0; comp < ghostInfo.Components.Length; ++comp)
        {
            if (ghostInfo.Components[comp].interpolatedClient)
            {
                ghostInterpolateComponents +=
                    k_GhostSpawnComponentTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        ghostInfo.Components[comp].name);
            }
            if (ghostInfo.Components[comp].predictedClient)
            {
                ghostPredictComponents +=
                    k_GhostSpawnComponentTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        ghostInfo.Components[comp].name);
            }
        }

        var spawnData = k_GhostSpawnSystemTemplate
            .Replace("$(GHOSTNAME)", target.name)
            .Replace("$(GHOSTINTERPOLATEDCOMPONENTS)", ghostInterpolateComponents)
            .Replace("$(GHOSTPREDICTEDCOMPONENTS)", ghostPredictComponents);
        File.WriteAllText(Path.Combine(assetPath, ghostInfo.SpawnSystemPath), spawnData);
    }

    private const string k_GhostUpdateSystemTemplate = @"using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

[UpdateInGroup(typeof(GhostUpdateSystemGroup))]
public class $(GHOSTNAME)GhostUpdateSystem : JobComponentSystem
{
    [BurstCompile]
    [RequireComponentTag(typeof($(GHOSTNAME)SnapshotData))]
    [ExcludeComponent(typeof(PredictedEntityComponent))]
    struct UpdateInterpolatedJob : IJobForEachWithEntity<$(GHOSTINTERPOLATEDCOMPONENTTYPES)>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<$(GHOSTNAME)SnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index$(GHOSTINTERPOLATEDCOMPONENTREFS))
        {
            var snapshot = snapshotFromEntity[entity];
            $(GHOSTNAME)SnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

$(GHOSTINTERPOLATEDASSIGNMENTS)
        }
    }
    [BurstCompile]
    [RequireComponentTag(typeof($(GHOSTNAME)SnapshotData), typeof(PredictedEntityComponent))]
    struct UpdatePredictedJob : IJobForEachWithEntity<$(GHOSTPREDICTEDCOMPONENTTYPES)>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<$(GHOSTNAME)SnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index$(GHOSTPREDICTEDCOMPONENTREFS))
        {
            var snapshot = snapshotFromEntity[entity];
            $(GHOSTNAME)SnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

$(GHOSTPREDICTEDASSIGNMENTS)
        }
    }
    private NetworkTimeSystem m_NetworkTimeSystem;
    protected override void OnCreateManager()
    {
        m_NetworkTimeSystem = World.GetOrCreateSystem<NetworkTimeSystem>();
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var updateInterpolatedJob = new UpdateInterpolatedJob
        {
            snapshotFromEntity = GetBufferFromEntity<$(GHOSTNAME)SnapshotData>(),
            targetTick = m_NetworkTimeSystem.interpolateTargetTick
        };
        var updatePredictedJob = new UpdatePredictedJob
        {
            snapshotFromEntity = GetBufferFromEntity<$(GHOSTNAME)SnapshotData>(),
            targetTick = m_NetworkTimeSystem.predictTargetTick
        };
        inputDeps = updateInterpolatedJob.Schedule(this, inputDeps);
        return updatePredictedJob.Schedule(this, inputDeps);
    }
}
";

    private const string k_GhostUpdateComponentRefTemplate = @",
            ref $(GHOSTCOMPONENT) ghost$(GHOSTCOMPONENT)";
    private const string k_GhostUpdateAssignTemplate = @"            ghost$(GHOSTCOMPONENT).$(GHOSTFIELD) = snapshotData.Get$(GHOSTCOMPONENT)$(GHOSTFIELD)();
";
    void GenerateUpdateSystem(GhostAuthoringComponent ghostInfo)
    {
        var ghostInterpolatedComponentRefs = "";
        var ghostPredictedComponentRefs = "";
        var ghostInterpolatedComponentTypes = "";
        var ghostPredictedComponentTypes = "";
        var ghostInterpolatedAssignments = "";
        var ghostPredictedAssignments = "";
        for (int comp = 0; comp < ghostInfo.Components.Length; ++comp)
        {
            if (ghostInfo.Components[comp].fields.Length > 0)
            {
                if (ghostInfo.Components[comp].interpolatedClient)
                {
                    if (ghostInterpolatedComponentTypes != "")
                        ghostInterpolatedComponentTypes += ", ";

                    ghostInterpolatedComponentTypes += ghostInfo.Components[comp].name;
                    ghostInterpolatedComponentRefs +=
                        k_GhostUpdateComponentRefTemplate.Replace("$(GHOSTCOMPONENT)",
                            ghostInfo.Components[comp].name);
                    for (int field = 0; field < ghostInfo.Components[comp].fields.Length; ++field)
                    {
                        ghostInterpolatedAssignments +=
                            k_GhostUpdateAssignTemplate.Replace("$(GHOSTCOMPONENT)",
                                    ghostInfo.Components[comp].name)
                                .Replace("$(GHOSTFIELD)",
                                    ghostInfo.Components[comp].fields[field].name);
                    }
                }
                if (ghostInfo.Components[comp].predictedClient)
                {
                    if (ghostPredictedComponentTypes != "")
                        ghostPredictedComponentTypes += ", ";

                    ghostPredictedComponentTypes += ghostInfo.Components[comp].name;
                    ghostPredictedComponentRefs +=
                        k_GhostUpdateComponentRefTemplate.Replace("$(GHOSTCOMPONENT)",
                            ghostInfo.Components[comp].name);
                    for (int field = 0; field < ghostInfo.Components[comp].fields.Length; ++field)
                    {
                        ghostPredictedAssignments +=
                            k_GhostUpdateAssignTemplate.Replace("$(GHOSTCOMPONENT)",
                                    ghostInfo.Components[comp].name)
                                .Replace("$(GHOSTFIELD)",
                                    ghostInfo.Components[comp].fields[field].name);
                    }
                }
            }
        }
        var updateData = k_GhostUpdateSystemTemplate
            .Replace("$(GHOSTNAME)", target.name)
            .Replace("$(GHOSTINTERPOLATEDCOMPONENTREFS)", ghostInterpolatedComponentRefs)
            .Replace("$(GHOSTPREDICTEDCOMPONENTREFS)", ghostPredictedComponentRefs)
            .Replace("$(GHOSTINTERPOLATEDCOMPONENTTYPES)", ghostInterpolatedComponentTypes)
            .Replace("$(GHOSTPREDICTEDCOMPONENTTYPES)", ghostPredictedComponentTypes)
            .Replace("$(GHOSTINTERPOLATEDASSIGNMENTS)", ghostInterpolatedAssignments)
            .Replace("$(GHOSTPREDICTEDASSIGNMENTS)", ghostPredictedAssignments);
        File.WriteAllText(Path.Combine(assetPath, ghostInfo.UpdateSystemPath), updateData);
    }
}
