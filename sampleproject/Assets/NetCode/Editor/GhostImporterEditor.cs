using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine;

[CustomEditor(typeof(GhostImporter))]
public class GhostImporterEditor : ScriptedImporterEditor
{
    [System.Serializable]
    public struct GhostField
    {
        public string name;
        public int quantization;
        public bool interpolate;
        internal System.Type dataType;
    }
    [System.Serializable]
    public struct GhostComponent
    {
        public string name;
        public bool interpolatedClient;
        public bool predictedClient;
        public bool server;
        public GhostField[] fields;
        internal bool expanded;
    }

    [System.Serializable]
    public struct GhostInfo
    {
        public string snapshotDataPath;
        public string spawnSystemPath;
        public string updateSystemPath;
        public string serializerPath;
        public string importance;
        public bool transform2d;
        public GhostComponent[] components;
    }

    private GhostInfo m_GhostInfo;
    public override void OnEnable()
    {
        base.OnEnable();

        try
        {
            m_GhostInfo = JsonUtility.FromJson<GhostInfo>(File.ReadAllText(AssetDatabase.GetAssetPath(assetTarget)));
        }
        catch
        {
            m_GhostInfo = default(GhostInfo);
        }

        var assetTargetName = assetTarget==null?"":assetTarget.name;
        if (m_GhostInfo.snapshotDataPath == null)
            m_GhostInfo.snapshotDataPath = assetTargetName + "SnapshotData.cs";
        if (m_GhostInfo.spawnSystemPath == null)
            m_GhostInfo.spawnSystemPath = assetTargetName + "GhostSpawnSystem.cs";
        if (m_GhostInfo.updateSystemPath == null)
            m_GhostInfo.updateSystemPath = assetTargetName + "GhostUpdateSystem.cs";
        if (m_GhostInfo.serializerPath == null)
            m_GhostInfo.serializerPath = assetTargetName + "GhostSerializer.cs";
        if (m_GhostInfo.importance == null)
            m_GhostInfo.importance = "1";
        if (m_GhostInfo.components == null)
            m_GhostInfo.components = new GhostComponent[0];
        for (int i = 0; i < m_GhostInfo.components.Length; ++i)
        {
            if (m_GhostInfo.components[i].name == null)
                m_GhostInfo.components[i].name = "";
            if (m_GhostInfo.components[i].fields == null)
                m_GhostInfo.components[i].fields = new GhostField[0];
            for (int j = 0; j < m_GhostInfo.components[i].fields.Length; ++j)
            {
                if (m_GhostInfo.components[i].fields[j].name == null)
                    m_GhostInfo.components[i].fields[j].name = "";
            }
        }
    }

    private Vector2 m_ScrollPos;
    public override void OnInspectorGUI()
    {
        m_GhostInfo.snapshotDataPath = EditorGUILayout.TextField("SnapshotData path", m_GhostInfo.snapshotDataPath);
        m_GhostInfo.spawnSystemPath = EditorGUILayout.TextField("SpawnSystem path", m_GhostInfo.spawnSystemPath);
        m_GhostInfo.updateSystemPath = EditorGUILayout.TextField("UpdateSystem path", m_GhostInfo.updateSystemPath);
        m_GhostInfo.serializerPath = EditorGUILayout.TextField("Serializer path", m_GhostInfo.serializerPath);
        m_GhostInfo.importance = EditorGUILayout.TextField("Importance", m_GhostInfo.importance);
        m_GhostInfo.transform2d = EditorGUILayout.Toggle("Force 2d transforms", m_GhostInfo.transform2d);
        for (int i = 0; i < m_GhostInfo.components.Length; ++i)
        {
            var title = m_GhostInfo.components[i].name + " (";
            if (m_GhostInfo.components[i].server)
                title += "S";
            else
                title += "-";
            title += "/";
            if (m_GhostInfo.components[i].interpolatedClient)
                title += "IC";
            else
                title += "-";
            title += "/";
            if (m_GhostInfo.components[i].predictedClient)
                title += "PC";
            else
                title += "-";
            title += ")";
            m_GhostInfo.components[i].expanded = EditorGUILayout.BeginFoldoutHeaderGroup(m_GhostInfo.components[i].expanded, title);
            if (m_GhostInfo.components[i].expanded)
            {
                m_GhostInfo.components[i].name =
                    EditorGUILayout.TextField("Component name", m_GhostInfo.components[i].name);
                m_GhostInfo.components[i].server = GUILayout.Toggle(m_GhostInfo.components[i].server, "Server");
                m_GhostInfo.components[i].interpolatedClient =
                    GUILayout.Toggle(m_GhostInfo.components[i].interpolatedClient, "Interpolated Client");
                m_GhostInfo.components[i].predictedClient =
                    GUILayout.Toggle(m_GhostInfo.components[i].predictedClient, "Predicted Client");

                for (int j = 0; j < m_GhostInfo.components[i].fields.Length; ++j)
                {
                    m_GhostInfo.components[i].fields[j].name =
                        EditorGUILayout.TextField("Field name", m_GhostInfo.components[i].fields[j].name);
                    m_GhostInfo.components[i].fields[j].quantization = EditorGUILayout.IntField("Quantization scale",
                        m_GhostInfo.components[i].fields[j].quantization);
                    m_GhostInfo.components[i].fields[j].interpolate = EditorGUILayout.Toggle("Interpolate",
                        m_GhostInfo.components[i].fields[j].interpolate);
                }

                if (GUILayout.Button("Add Field"))
                {
                    var fields = new GhostField[m_GhostInfo.components[i].fields.Length + 1];
                    for (int j = 0; j < m_GhostInfo.components[i].fields.Length; ++j)
                        fields[j] = m_GhostInfo.components[i].fields[j];
                    m_GhostInfo.components[i].fields = fields;
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        if (GUILayout.Button("Add component"))
        {
            var comps = new GhostComponent[m_GhostInfo.components.Length + 1];
            for (int i = 0; i < m_GhostInfo.components.Length; ++i)
                comps[i] = m_GhostInfo.components[i];
            comps[m_GhostInfo.components.Length].fields = new GhostField[0];
            comps[m_GhostInfo.components.Length].interpolatedClient = true;
            comps[m_GhostInfo.components.Length].predictedClient = true;
            comps[m_GhostInfo.components.Length].server = true;
            m_GhostInfo.components = comps;
        }
        if (GUILayout.Button("Save"))
        {
            File.WriteAllText(AssetDatabase.GetAssetPath(assetTarget), JsonUtility.ToJson(m_GhostInfo, true));
        }
        if (GUILayout.Button("Generate code"))
        {
            GenerateGhost();
        }
        ApplyRevertGUI();
    }

    // ApplyRevertGUI must be called to avoid errors in 19.2, we do not actually edit the settings so just work around the issue for now
    protected override bool OnApplyRevertGUI()
    {
        return false;
    }

    void GenerateGhost()
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

            allTypes.AddRange(asmTypes.Where(t=>typeof(IComponentData).IsAssignableFrom(t) || typeof(IBufferElementData).IsAssignableFrom(t)));
        }

        // Update type of all fields
        for (int comp = 0; comp < m_GhostInfo.components.Length; ++comp)
        {

            var componentTypes = allTypes.Where(t => t.Name == m_GhostInfo.components[comp].name);
            if (componentTypes.Count() != 1)
            {
                Debug.LogError("Could not find the type or found several candidates: " + m_GhostInfo.components[comp].name);
                return;
            }
            System.Type componentType = componentTypes.First();
            for (int field = 0; field < m_GhostInfo.components[comp].fields.Length; ++field)
            {
                var fieldInfo = componentType.GetField(m_GhostInfo.components[comp].fields[field].name);
                if (fieldInfo == null)
                {
                    Debug.LogError("Could not find field: " + m_GhostInfo.components[comp].fields[field].name + " in componentType: " + m_GhostInfo.components[comp].name);
                    return;
                }
                m_GhostInfo.components[comp].fields[field].dataType = fieldInfo.FieldType;
            }
        }

        GenerateSnapshotData();
        GenerateSerializer();
        GenerateSpawnSystem();
        GenerateUpdateSystem();
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

    void GenerateSnapshotData()
    {
        var fields = "";
        var fieldsGetSet = "";
        var predict = "";
        var read = "";
        var write = "";
        var interpolate = "";
        var typeProviders = new List<GhostSnapshotValue>();
        if (m_GhostInfo.transform2d)
        {
            typeProviders.Add(new GhostSnapshotValue2DRotation());
            typeProviders.Add(new GhostSnapshotValue2DTranslation());

        }
        typeProviders.Add(new GhostSnapshotValueQuaternion());
        typeProviders.Add(new GhostSnapshotValueFloat());
        typeProviders.Add(new GhostSnapshotValueFloat2());
        typeProviders.Add(new GhostSnapshotValueFloat3());
        typeProviders.Add(new GhostSnapshotValueFloat4());
        typeProviders.Add(new GhostSnapshotValueInt());
        typeProviders.Add(new GhostSnapshotValueUInt());
        for (int comp = 0; comp < m_GhostInfo.components.Length; ++comp)
        {
            for (int field = 0; field < m_GhostInfo.components[comp].fields.Length; ++field)
            {
                bool processed = false;
                foreach (var value in typeProviders)
                {
                    if (value.CanProcess(m_GhostInfo.components[comp].fields[field].dataType,
                        m_GhostInfo.components[comp].name,
                        m_GhostInfo.components[comp].fields[field].name))
                    {
                        fields += value.GenerateMembers(m_GhostInfo.components[comp].name,
                            m_GhostInfo.components[comp].fields[field].name);
                        fieldsGetSet += value.GenerateGetSet(m_GhostInfo.components[comp].name,
                            m_GhostInfo.components[comp].fields[field].name,
                            m_GhostInfo.components[comp].fields[field].quantization);
                        predict += value.GeneratePredict(m_GhostInfo.components[comp].name,
                            m_GhostInfo.components[comp].fields[field].name);
                        read += value.GenerateRead(m_GhostInfo.components[comp].name,
                            m_GhostInfo.components[comp].fields[field].name);
                        write += value.GenerateWrite(m_GhostInfo.components[comp].name,
                            m_GhostInfo.components[comp].fields[field].name);
                        if (m_GhostInfo.components[comp].fields[field].interpolate)
                        {
                            interpolate += value.GenerateInterpolate(m_GhostInfo.components[comp].name,
                                m_GhostInfo.components[comp].fields[field].name);
                        }

                        processed = true;
                        break;
                    }
                }
                if (!processed)
                {
                    Debug.LogError("Unhandled type " + m_GhostInfo.components[comp].fields[field].dataType);
                }
            }
        }

        var snapshotData = k_SnapshotDataTemplate
            .Replace("$(GHOSTNAME)", assetTarget.name)
            .Replace("$(GHOSTFIELDS)", fields)
            .Replace("$(GHOSTFIELDSGETSET)", fieldsGetSet)
            .Replace("$(GHOSTPREDICT)", predict)
            .Replace("$(GHOSTREAD)", read)
            .Replace("$(GHOSTWRITE)", write)
            .Replace("$(GHOSTINTERPOLATE)", interpolate);


        File.WriteAllText(Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(assetTarget)), m_GhostInfo.snapshotDataPath), snapshotData);
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

    void GenerateSerializer()
    {
        string ghostComponentType = "";
        string assignGhostComponentType = "";
        string ghostComponentTypeCheck = "";
        string ghostApply = "";
        int serverComponentCount = 0;
        for (int comp = 0; comp < m_GhostInfo.components.Length; ++comp)
        {
            if (!m_GhostInfo.components[comp].server)
                continue;
            ++serverComponentCount;
            ghostComponentTypeCheck +=
                k_SerializerComponentTypeCheckTemplate.Replace("$(GHOSTCOMPONENTTYPE)", m_GhostInfo.components[comp].name);
            ghostComponentType +=
                k_SerializerComponentTypeTemplate.Replace("$(GHOSTCOMPONENTTYPE)", m_GhostInfo.components[comp].name);
            assignGhostComponentType +=
                k_SerializerAssignComponentTypeTemplate.Replace("$(GHOSTCOMPONENTTYPE)", m_GhostInfo.components[comp].name);
            if (m_GhostInfo.components[comp].fields.Length > 0)
            {
                ghostComponentType +=
                    k_SerializerComponentTypeDataTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        m_GhostInfo.components[comp].name);
                assignGhostComponentType +=
                    k_SerializerAssignComponentTypeDataTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        m_GhostInfo.components[comp].name);
                ghostApply +=
                    k_SerializerAssignChunkArrayTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        m_GhostInfo.components[comp].name);
                for (int field = 0; field < m_GhostInfo.components[comp].fields.Length; ++field)
                {
                    ghostApply +=
                        k_SerializerAssignSnapshotTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                            m_GhostInfo.components[comp].name)
                            .Replace("$(GHOSTFIELDNAME)",
                                m_GhostInfo.components[comp].fields[field].name);
                }
            }
        }

        var serializerData = k_SerializerTemplate
            .Replace("$(GHOSTNAME)", assetTarget.name)
            .Replace("$(GHOSTIMPORTANCE)", m_GhostInfo.importance)
            .Replace("$(GHOSTCOMPONENTCHECK)", ghostComponentTypeCheck)
            .Replace("$(GHOSTAPPLY)", ghostApply)
            .Replace("$(GHOSTCOMPONENTCOUNT)", serverComponentCount.ToString())
            .Replace("$(ASSIGNGHOSTCOMPONENTTYPES)", assignGhostComponentType)
            .Replace("$(GHOSTCOMPONENTTYPES)", ghostComponentType);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(assetTarget)), m_GhostInfo.serializerPath), serializerData);
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
    void GenerateSpawnSystem()
    {
        string ghostInterpolateComponents = "";
        string ghostPredictComponents = "";
        for (int comp = 0; comp < m_GhostInfo.components.Length; ++comp)
        {
            if (m_GhostInfo.components[comp].interpolatedClient)
            {
                ghostInterpolateComponents +=
                    k_GhostSpawnComponentTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        m_GhostInfo.components[comp].name);
            }
            if (m_GhostInfo.components[comp].predictedClient)
            {
                ghostPredictComponents +=
                    k_GhostSpawnComponentTemplate.Replace("$(GHOSTCOMPONENTTYPE)",
                        m_GhostInfo.components[comp].name);
            }
        }

        var spawnData = k_GhostSpawnSystemTemplate
            .Replace("$(GHOSTNAME)", assetTarget.name)
            .Replace("$(GHOSTINTERPOLATEDCOMPONENTS)", ghostInterpolateComponents)
            .Replace("$(GHOSTPREDICTEDCOMPONENTS)", ghostPredictComponents);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(assetTarget)), m_GhostInfo.spawnSystemPath), spawnData);
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
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var updateInterpolatedJob = new UpdateInterpolatedJob
        {
            snapshotFromEntity = GetBufferFromEntity<$(GHOSTNAME)SnapshotData>(),
            targetTick = NetworkTimeSystem.interpolateTargetTick
        };
        var updatePredictedJob = new UpdatePredictedJob
        {
            snapshotFromEntity = GetBufferFromEntity<$(GHOSTNAME)SnapshotData>(),
            targetTick = NetworkTimeSystem.predictTargetTick
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
    void GenerateUpdateSystem()
    {
        var ghostInterpolatedComponentRefs = "";
        var ghostPredictedComponentRefs = "";
        var ghostInterpolatedComponentTypes = "";
        var ghostPredictedComponentTypes = "";
        var ghostInterpolatedAssignments = "";
        var ghostPredictedAssignments = "";
        for (int comp = 0; comp < m_GhostInfo.components.Length; ++comp)
        {
            if (m_GhostInfo.components[comp].fields.Length > 0)
            {
                if (m_GhostInfo.components[comp].interpolatedClient)
                {
                    if (ghostInterpolatedComponentTypes != "")
                        ghostInterpolatedComponentTypes += ", ";

                    ghostInterpolatedComponentTypes += m_GhostInfo.components[comp].name;
                    ghostInterpolatedComponentRefs +=
                        k_GhostUpdateComponentRefTemplate.Replace("$(GHOSTCOMPONENT)",
                            m_GhostInfo.components[comp].name);
                    for (int field = 0; field < m_GhostInfo.components[comp].fields.Length; ++field)
                    {
                        ghostInterpolatedAssignments +=
                            k_GhostUpdateAssignTemplate.Replace("$(GHOSTCOMPONENT)",
                                    m_GhostInfo.components[comp].name)
                                .Replace("$(GHOSTFIELD)",
                                    m_GhostInfo.components[comp].fields[field].name);
                    }
                }
                if (m_GhostInfo.components[comp].predictedClient)
                {
                    if (ghostPredictedComponentTypes != "")
                        ghostPredictedComponentTypes += ", ";

                    ghostPredictedComponentTypes += m_GhostInfo.components[comp].name;
                    ghostPredictedComponentRefs +=
                        k_GhostUpdateComponentRefTemplate.Replace("$(GHOSTCOMPONENT)",
                            m_GhostInfo.components[comp].name);
                    for (int field = 0; field < m_GhostInfo.components[comp].fields.Length; ++field)
                    {
                        ghostPredictedAssignments +=
                            k_GhostUpdateAssignTemplate.Replace("$(GHOSTCOMPONENT)",
                                    m_GhostInfo.components[comp].name)
                                .Replace("$(GHOSTFIELD)",
                                    m_GhostInfo.components[comp].fields[field].name);
                    }
                }
            }
        }
        var updateData = k_GhostUpdateSystemTemplate
            .Replace("$(GHOSTNAME)", assetTarget.name)
            .Replace("$(GHOSTINTERPOLATEDCOMPONENTREFS)", ghostInterpolatedComponentRefs)
            .Replace("$(GHOSTPREDICTEDCOMPONENTREFS)", ghostPredictedComponentRefs)
            .Replace("$(GHOSTINTERPOLATEDCOMPONENTTYPES)", ghostInterpolatedComponentTypes)
            .Replace("$(GHOSTPREDICTEDCOMPONENTTYPES)", ghostPredictedComponentTypes)
            .Replace("$(GHOSTINTERPOLATEDASSIGNMENTS)", ghostInterpolatedAssignments)
            .Replace("$(GHOSTPREDICTEDASSIGNMENTS)", ghostPredictedAssignments);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(assetTarget)), m_GhostInfo.updateSystemPath), updateData);
    }
}
