using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class GhostCollectionGeneratorWindow : EditorWindow
{
    private const string GhostSerializerCollectionTemplate = @"using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
public struct /*$GHOST_COLLECTION_PREFIX*/GhostSerializerCollection : IGhostSerializerCollection
{
    public int FindSerializer(EntityArchetype arch)
    {
/*$GHOST_FIND_CHECKS*/
        throw new ArgumentException(""Invalid serializer type"");
    }

    public void BeginSerialize(ComponentSystemBase system)
    {
/*$GHOST_BEGIN_SERIALIZE*/
    }

    public int CalculateImportance(int serializer, ArchetypeChunk chunk)
    {
        switch (serializer)
        {
/*$GHOST_CALCULATE_IMPORTANCE*/
        }

        throw new ArgumentException(""Invalid serializer type"");
    }

    public bool WantsPredictionDelta(int serializer)
    {
        switch (serializer)
        {
/*$GHOST_WANTS_PREDICTION_DELTA*/
        }

        throw new ArgumentException(""Invalid serializer type"");
    }

    public int GetSnapshotSize(int serializer)
    {
        switch (serializer)
        {
/*$GHOST_SNAPSHOT_SIZE*/
        }

        throw new ArgumentException(""Invalid serializer type"");
    }

    public unsafe int Serialize(int serializer, ArchetypeChunk chunk, int startIndex, uint currentTick,
        Entity* currentSnapshotEntity, void* currentSnapshotData,
        GhostSystemStateComponent* ghosts, NativeArray<Entity> ghostEntities,
        NativeArray<int> baselinePerEntity, NativeList<SnapshotBaseline> availableBaselines,
        DataStreamWriter dataStream, NetworkCompressionModel compressionModel)
    {
        switch (serializer)
        {
/*$GHOST_INVOKE_SERIALIZE*/
            default:
                throw new ArgumentException(""Invalid serializer type"");
        }
    }
/*$GHOST_SERIALIZER_INSTANCES*/
}

public class /*$GHOST_SYSTEM_PREFIX*/GhostSendSystem : GhostSendSystem<GhostSerializerCollection>
{
}
";
    private const string GhostDeserializerCollectionTemplate = @"using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
public struct /*$GHOST_COLLECTION_PREFIX*/GhostDeserializerCollection : IGhostDeserializerCollection
{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    public string[] CreateSerializerNameList()
    {
        var arr = new string[]
        {
/*$GHOST_SERIALIZER_NAMES*/
        };
        return arr;
    }

    public int Length => /*$GHOST_SERIALIZER_COUNT*/;
#endif
    public void Initialize(World world)
    {
/*$GHOST_INITIALIZE_DESERIALIZE*/
    }

    public void BeginDeserialize(JobComponentSystem system)
    {
/*$GHOST_BEGIN_DESERIALIZE*/
    }
    public void Deserialize(int serializer, Entity entity, uint snapshot, uint baseline, uint baseline2, uint baseline3,
        DataStreamReader reader,
        ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
    {
        switch (serializer)
        {
/*$GHOST_INVOKE_DESERIALIZE*/
        default:
            throw new ArgumentException(""Invalid serializer type"");
        }
    }
    public void Spawn(int serializer, int ghostId, uint snapshot, DataStreamReader reader,
        ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
    {
        switch (serializer)
        {
/*$GHOST_INVOKE_SPAWN*/
            default:
                throw new ArgumentException(""Invalid serializer type"");
        }
    }

/*$GHOST_DESERIALIZER_INSTANCES*/
}
public class /*$GHOST_SYSTEM_PREFIX*/GhostReceiveSystem : GhostReceiveSystem<GhostDeserializerCollection>
{
}
";

    private const string GhostSerializerInstanceTemplate = @"    private /*$GHOST_SERIALIZER_TYPE*/ m_/*$GHOST_SERIALIZER_TYPE*/;
";
    private const string GhostDeserializerInstanceTemplate = @"    private BufferFromEntity</*$GHOST_SNAPSHOT_TYPE*/> m_/*$GHOST_SNAPSHOT_TYPE*/FromEntity;
    private NativeList<int> m_/*$GHOST_SNAPSHOT_TYPE*/NewGhostIds;
    private NativeList</*$GHOST_SNAPSHOT_TYPE*/> m_/*$GHOST_SNAPSHOT_TYPE*/NewGhosts;
";
    private const string GhostFindTemplate = @"        if (m_/*$GHOST_SERIALIZER_TYPE*/.CanSerialize(arch))
            return /*$GHOST_SERIALIZER_INDEX*/;
";

    private const string GhostBeginSerializeTemplate = @"        m_/*$GHOST_SERIALIZER_TYPE*/.BeginSerialize(system);
";

    private const string GhostCalculateImportanceTemplate = @"            case /*$GHOST_SERIALIZER_INDEX*/:
                return m_/*$GHOST_SERIALIZER_TYPE*/.CalculateImportance(chunk);
";
    private const string GhostWantsPredictionDeltaTemplate = @"            case /*$GHOST_SERIALIZER_INDEX*/:
                return m_/*$GHOST_SERIALIZER_TYPE*/.WantsPredictionDelta;
";
    // FIXME: generate the sizeof(snapshot data) ?
    private const string GhostSnapshotSizeTemplate = @"            case /*$GHOST_SERIALIZER_INDEX*/:
                return m_/*$GHOST_SERIALIZER_TYPE*/.SnapshotSize;
";
    private const string GhostInvokeSerializeTemplate = @"            case /*$GHOST_SERIALIZER_INDEX*/:
            {
                return GhostSendSystem</*$GHOST_COLLECTION_PREFIX*/GhostSerializerCollection>.InvokeSerialize(m_/*$GHOST_SERIALIZER_TYPE*/, serializer,
                    chunk, startIndex, currentTick, currentSnapshotEntity, (/*$GHOST_SNAPSHOT_TYPE*/*)currentSnapshotData,
                    ghosts, ghostEntities, baselinePerEntity, availableBaselines,
                    dataStream, compressionModel);
            }
";
    private const string GhostSerializerNameTemplate = @"            ""/*$GHOST_SERIALIZER_TYPE*/"",
";
    private const string GhostInitializeDeserializeTemplate = @"        var cur/*$GHOST_SPAWNER_TYPE*/ = world.GetOrCreateSystem</*$GHOST_SPAWNER_TYPE*/>();
        m_/*$GHOST_SNAPSHOT_TYPE*/NewGhostIds = cur/*$GHOST_SPAWNER_TYPE*/.NewGhostIds;
        m_/*$GHOST_SNAPSHOT_TYPE*/NewGhosts = cur/*$GHOST_SPAWNER_TYPE*/.NewGhosts;
        cur/*$GHOST_SPAWNER_TYPE*/.GhostType = /*$GHOST_SERIALIZER_INDEX*/;
";
    private const string GhostBeginDeserializeTemplate = @"        m_/*$GHOST_SNAPSHOT_TYPE*/FromEntity = system.GetBufferFromEntity</*$GHOST_SNAPSHOT_TYPE*/>();
";
    private const string GhostInvokeDeserializeTemplate = @"        case /*$GHOST_SERIALIZER_INDEX*/:
            GhostReceiveSystem</*$GHOST_COLLECTION_PREFIX*/GhostDeserializerCollection>.InvokeDeserialize(m_/*$GHOST_SNAPSHOT_TYPE*/FromEntity, entity, snapshot, baseline, baseline2,
                baseline3, reader, ref ctx, compressionModel);
            break;
";
    private const string GhostInvokeSpawnTemplate = @"            case /*$GHOST_SERIALIZER_INDEX*/:
                m_/*$GHOST_SNAPSHOT_TYPE*/NewGhostIds.Add(ghostId);
                m_/*$GHOST_SNAPSHOT_TYPE*/NewGhosts.Add(GhostReceiveSystem</*$GHOST_COLLECTION_PREFIX*/GhostDeserializerCollection>.InvokeSpawn</*$GHOST_SNAPSHOT_TYPE*/>(snapshot, reader, ref ctx, compressionModel));
                break;
";

    [MenuItem("Multiplayer/CodeGen/GhostCollection Generator")]
    public static void ShowWindow()
    {
        GetWindow<GhostCollectionGeneratorWindow>(false, "GhostCollection Generator", true);
    }

    class GhostType
    {
        public Type serializerType;
        public Type snapshotType;
        public Type spawnerType;
        public bool generate;
    }
    private List<GhostType> m_GhostTypes;
    private UnityEditorInternal.ReorderableList m_GhostList;
    private string serializerCollectionPath = "GhostSerializerCollection.cs";
    private string deserializerCollectionPath = "GhostDeserializerCollection.cs";
    public GhostCollectionGeneratorWindow()
    {
        m_GhostTypes = new List<GhostType>();
    }

    private void OnGUI()
    {
        serializerCollectionPath = EditorGUILayout.TextField("SerializerCollection path", serializerCollectionPath);
        deserializerCollectionPath = EditorGUILayout.TextField("DeserializerCollection path", deserializerCollectionPath);
        if (GUILayout.Button("Scan for ghosts"))
        {
            FindAllGhosts();
        }

        if (m_GhostList == null)
        {
            m_GhostList =
                new UnityEditorInternal.ReorderableList(m_GhostTypes, typeof(GhostType), true, false, false, false);
            m_GhostList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                m_GhostTypes[index].generate = EditorGUI.Toggle(rect, m_GhostTypes[index].serializerType.Name,
                    m_GhostTypes[index].generate);
            };
        }

        var listRect = new Rect(0, EditorGUIUtility.singleLineHeight * 6, position.width, EditorGUIUtility.singleLineHeight * (m_GhostTypes.Count + 1));
        m_GhostList.DoList(listRect);

        if (GUILayout.Button("Generate Collection"))
        {
            var serializerFile = Path.Combine(Application.dataPath, serializerCollectionPath);
            var deserializerFile = Path.Combine(Application.dataPath, deserializerCollectionPath);

            string ghostSerializerInst = "";
            string ghostDeserializerInst = "";
            string ghostFind = "";
            string ghostBeginSerialize = "";
            string ghostCalculateImportance = "";
            string ghostWantsPredictionDelta = "";
            string ghostSnapshotSize = "";
            string ghostInvokeSerialize = "";
            string ghostSerializerName = "";
            string ghostInitializeDeserialize = "";
            string ghostBeginDeserialize = "";
            string ghostInvokeDeserialize = "";
            string ghostInvokeSpawn = "";
            for (int i = 0; i < m_GhostTypes.Count; ++i)
            {
                if (m_GhostTypes[i].generate)
                {
                    ghostSerializerInst += GhostSerializerInstanceTemplate
                        .Replace("/*$GHOST_SERIALIZER_TYPE*/", m_GhostTypes[i].serializerType.Name);
                    ghostDeserializerInst += GhostDeserializerInstanceTemplate
                        .Replace("/*$GHOST_SNAPSHOT_TYPE*/", m_GhostTypes[i].snapshotType.Name);
                    ghostFind += GhostFindTemplate
                        .Replace("/*$GHOST_SERIALIZER_TYPE*/", m_GhostTypes[i].serializerType.Name)
                        .Replace("/*$GHOST_SERIALIZER_INDEX*/", i.ToString());
                    ghostBeginSerialize += GhostBeginSerializeTemplate
                        .Replace("/*$GHOST_SERIALIZER_TYPE*/", m_GhostTypes[i].serializerType.Name);
                    ghostCalculateImportance += GhostCalculateImportanceTemplate
                        .Replace("/*$GHOST_SERIALIZER_TYPE*/", m_GhostTypes[i].serializerType.Name)
                        .Replace("/*$GHOST_SERIALIZER_INDEX*/", i.ToString());
                    ghostWantsPredictionDelta += GhostWantsPredictionDeltaTemplate
                        .Replace("/*$GHOST_SERIALIZER_TYPE*/", m_GhostTypes[i].serializerType.Name)
                        .Replace("/*$GHOST_SERIALIZER_INDEX*/", i.ToString());
                    ghostSnapshotSize += GhostSnapshotSizeTemplate
                        .Replace("/*$GHOST_SERIALIZER_TYPE*/", m_GhostTypes[i].serializerType.Name)
                        .Replace("/*$GHOST_SERIALIZER_INDEX*/", i.ToString());
                    ghostInvokeSerialize += GhostInvokeSerializeTemplate
                        .Replace("/*$GHOST_COLLECTION_PREFIX*/", "")
                        .Replace("/*$GHOST_SNAPSHOT_TYPE*/", m_GhostTypes[i].snapshotType.Name)
                        .Replace("/*$GHOST_SERIALIZER_TYPE*/", m_GhostTypes[i].serializerType.Name)
                        .Replace("/*$GHOST_SERIALIZER_INDEX*/", i.ToString());
                    ghostSerializerName += GhostSerializerNameTemplate
                        .Replace("/*$GHOST_SERIALIZER_TYPE*/", m_GhostTypes[i].serializerType.Name);
                    ghostInitializeDeserialize += GhostInitializeDeserializeTemplate
                        .Replace("/*$GHOST_SNAPSHOT_TYPE*/", m_GhostTypes[i].snapshotType.Name)
                        .Replace("/*$GHOST_SERIALIZER_INDEX*/", i.ToString())
                        .Replace("/*$GHOST_SPAWNER_TYPE*/", m_GhostTypes[i].spawnerType.Name);
                    ghostBeginDeserialize += GhostBeginDeserializeTemplate
                        .Replace("/*$GHOST_SNAPSHOT_TYPE*/", m_GhostTypes[i].snapshotType.Name);
                    ghostInvokeDeserialize += GhostInvokeDeserializeTemplate
                        .Replace("/*$GHOST_COLLECTION_PREFIX*/", "")
                        .Replace("/*$GHOST_SNAPSHOT_TYPE*/", m_GhostTypes[i].snapshotType.Name)
                        .Replace("/*$GHOST_SERIALIZER_INDEX*/", i.ToString());
                    ghostInvokeSpawn += GhostInvokeSpawnTemplate
                        .Replace("/*$GHOST_COLLECTION_PREFIX*/", "")
                        .Replace("/*$GHOST_SNAPSHOT_TYPE*/", m_GhostTypes[i].snapshotType.Name)
                        .Replace("/*$GHOST_SERIALIZER_INDEX*/", i.ToString());
                }
            }
            string serializerContent = GhostSerializerCollectionTemplate
                .Replace("/*$GHOST_SERIALIZER_INSTANCES*/", ghostSerializerInst)
                .Replace("/*$GHOST_DESERIALIZER_INSTANCES*/", ghostDeserializerInst)
                .Replace("/*$GHOST_FIND_CHECKS*/", ghostFind)
                .Replace("/*$GHOST_BEGIN_SERIALIZE*/", ghostBeginSerialize)
                .Replace("/*$GHOST_CALCULATE_IMPORTANCE*/", ghostCalculateImportance)
                .Replace("/*$GHOST_WANTS_PREDICTION_DELTA*/", ghostWantsPredictionDelta)
                .Replace("/*$GHOST_SNAPSHOT_SIZE*/", ghostSnapshotSize)
                .Replace("/*$GHOST_INVOKE_SERIALIZE*/", ghostInvokeSerialize)
                .Replace("/*$GHOST_SERIALIZER_NAMES*/", ghostSerializerName)
                .Replace("/*$GHOST_INITIALIZE_DESERIALIZE*/", ghostInitializeDeserialize)
                .Replace("/*$GHOST_BEGIN_DESERIALIZE*/", ghostBeginDeserialize)
                .Replace("/*$GHOST_INVOKE_DESERIALIZE*/", ghostInvokeDeserialize)
                .Replace("/*$GHOST_INVOKE_SPAWN*/", ghostInvokeSpawn)
                .Replace("/*$GHOST_SERIALIZER_COUNT*/", m_GhostTypes.Count.ToString())
                .Replace("/*$GHOST_COLLECTION_PREFIX*/", "")
                .Replace("/*$GHOST_SYSTEM_PREFIX*/", Application.productName);
            string deserializerContent = GhostDeserializerCollectionTemplate
                .Replace("/*$GHOST_SERIALIZER_INSTANCES*/", ghostSerializerInst)
                .Replace("/*$GHOST_DESERIALIZER_INSTANCES*/", ghostDeserializerInst)
                .Replace("/*$GHOST_FIND_CHECKS*/", ghostFind)
                .Replace("/*$GHOST_BEGIN_SERIALIZE*/", ghostBeginSerialize)
                .Replace("/*$GHOST_CALCULATE_IMPORTANCE*/", ghostCalculateImportance)
                .Replace("/*$GHOST_WANTS_PREDICTION_DELTA*/", ghostWantsPredictionDelta)
                .Replace("/*$GHOST_SNAPSHOT_SIZE*/", ghostSnapshotSize)
                .Replace("/*$GHOST_INVOKE_SERIALIZE*/", ghostInvokeSerialize)
                .Replace("/*$GHOST_SERIALIZER_NAMES*/", ghostSerializerName)
                .Replace("/*$GHOST_INITIALIZE_DESERIALIZE*/", ghostInitializeDeserialize)
                .Replace("/*$GHOST_BEGIN_DESERIALIZE*/", ghostBeginDeserialize)
                .Replace("/*$GHOST_INVOKE_DESERIALIZE*/", ghostInvokeDeserialize)
                .Replace("/*$GHOST_INVOKE_SPAWN*/", ghostInvokeSpawn)
                .Replace("/*$GHOST_SERIALIZER_COUNT*/", m_GhostTypes.Count.ToString())
                .Replace("/*$GHOST_COLLECTION_PREFIX*/", "")
                .Replace("/*$GHOST_SYSTEM_PREFIX*/", Application.productName);
            File.WriteAllText(serializerFile, serializerContent);
            File.WriteAllText(deserializerFile, deserializerContent);
        }
    }
    void FindAllGhosts()
    {
        m_GhostTypes.Clear();
        m_GhostList = null;
        var snapshotTypes = new List<Type>();
        var serializerTypes = new List<Type>();
        var spawnerTypes = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            IEnumerable<Type> allTypes;

            try
            {
                allTypes = assembly.GetTypes();

            }
            catch (ReflectionTypeLoadException e)
            {
                allTypes = e.Types.Where(t => t != null);
                Debug.LogWarning(
                    $"GhostCollectionGenerator failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
            }

            snapshotTypes.AddRange(allTypes.Where(t => !t.IsAbstract && !t.ContainsGenericParameters &&
                                                      t.GetInterfaces().Any(iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ISnapshotData<>))));
            serializerTypes.AddRange(allTypes.Where(t => !t.IsAbstract && !t.ContainsGenericParameters &&
                t.GetInterfaces().Any(iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IGhostSerializer<>))));
            spawnerTypes.AddRange(allTypes.Where(t => !t.IsAbstract &&
                                                   !t.ContainsGenericParameters && t.BaseType != null &&
                                                   t.BaseType.IsGenericType && t.BaseType.GetGenericTypeDefinition() ==
                                                   typeof(DefaultGhostSpawnSystem<>)));
        }
        foreach (var snapType in snapshotTypes)
        {
            var serializerType = serializerTypes.Where(t =>
                t.GetInterfaces().First(iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IGhostSerializer<>)).GenericTypeArguments[0] == snapType);
            if (serializerType.Count() == 0)
                Debug.LogWarning("Skipping ghost type " + snapType + " because it does not have a serializer");
            else if (serializerType.Count() > 1)
                Debug.LogWarning("Skipping ghost type " + snapType + " because it does has multiple serializers");
            var spawnType = spawnerTypes.Where(t =>
                t.BaseType.GenericTypeArguments[0] == snapType);
            if (spawnType.Count() == 0)
                Debug.LogWarning("Skipping ghost type " + snapType + " because it does not have a spawner");
            else if (spawnType.Count() > 1)
                Debug.LogWarning("Skipping ghost type " + snapType + " because it does has multiple spawners");
            if (spawnType.Count() == 1 && serializerType.Count() == 1)
                m_GhostTypes.Add(new GhostType {serializerType = serializerType.First(), snapshotType = snapType, spawnerType = spawnType.First(), generate = true});
        }
    }
}
