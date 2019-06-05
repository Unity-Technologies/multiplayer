using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class RpcCollectionGeneratorWindow : EditorWindow
{
    private const string RpcCollectionTemplate = @"using System;
using Unity.Entities;
using Unity.Networking.Transport;

public struct /*$RPC_COLLECTION_PREFIX*/RpcCollection : IRpcCollection
{
    static Type[] s_RpcTypes = new Type[]
    {
/*$RPC_TYPE_LIST*/
    };
    public void ExecuteRpc(int type, DataStreamReader reader, ref DataStreamReader.Context ctx, Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
    {
        switch (type)
        {
/*$RPC_TYPE_CASES*/
        }
    }

    public int GetRpcFromType<T>() where T : struct, IRpcCommand
    {
        for (int i = 0; i < s_RpcTypes.Length; ++i)
        {
            if (s_RpcTypes[i] == typeof(T))
                return i;
        }

        return -1;
    }
}

public class /*$RPC_SYSTEM_PREFIX*/RpcSystem : RpcSystem</*$RPC_COLLECTION_PREFIX*/RpcCollection>
{
}
";

    private const string RpcCaseTemplate = @"            case /*$RPC_CASE_NUM*/:
            {
                var tmp = new /*$RPC_CASE_TYPE*/();
                tmp.Deserialize(reader, ref ctx);
                tmp.Execute(connection, commandBuffer, jobIndex);
                break;
            }
";

    private const string RpcTypeTemplate = @"        typeof(/*$RPC_TYPE*/),
";
    [MenuItem("Multiplayer/CodeGen/RpcCollection Generator")]
    public static void ShowWindow()
    {
        GetWindow<RpcCollectionGeneratorWindow>(false, "RpcCollection Generator", true);
    }

    class RpcType
    {
        public Type type;
        public bool generate;
    }
    private List<RpcType> m_RpcTypes;
    public RpcCollectionGeneratorWindow()
    {
        m_RpcTypes = new List<RpcType>();
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Scan for Rpcs"))
        {
            FindAllRpcs();
        }

        for (int i = 0; i < m_RpcTypes.Count; ++i)
        {
            m_RpcTypes[i].generate = GUILayout.Toggle(m_RpcTypes[i].generate, m_RpcTypes[i].type.Name);
        }

        if (GUILayout.Button("Generate Collection"))
        {
            var dstFile = EditorUtility.SaveFilePanel("Select file to save", "", "RpcCollection", "cs");

            string rpcCases = "";
            string rpcTypes = "";
            for (int i = 0; i < m_RpcTypes.Count; ++i)
            {
                if (m_RpcTypes[i].generate)
                {
                    rpcCases += RpcCaseTemplate
                        .Replace("/*$RPC_CASE_NUM*/", i.ToString())
                        .Replace("/*$RPC_CASE_TYPE*/", m_RpcTypes[i].type.Name);
                    rpcTypes += RpcTypeTemplate.Replace("/*$RPC_TYPE*/", m_RpcTypes[i].type.Name);
                }
            }

            string content = RpcCollectionTemplate
                .Replace("/*$RPC_TYPE_CASES*/", rpcCases)
                .Replace("/*$RPC_TYPE_LIST*/", rpcTypes)
                .Replace("/*$RPC_COLLECTION_PREFIX*/", "")
                .Replace("/*$RPC_SYSTEM_PREFIX*/", Application.productName);
            File.WriteAllText(dstFile, content);
        }
    }
    void FindAllRpcs()
    {
        m_RpcTypes.Clear();
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
                    $"RpcCollectionGenerator failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
            }

            var pipelineTypes = allTypes.Where(t =>
                typeof(IRpcCommand).IsAssignableFrom(t) &&
                !t.IsAbstract && t.IsPublic &&
                !t.ContainsGenericParameters);

            foreach (var pt in pipelineTypes)
            {
                m_RpcTypes.Add(new RpcType {type = pt, generate = true});
            }
        }
    }
}