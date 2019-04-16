using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Networking.Transport;
using UnityEditor;
using UnityEngine;

public class PipelineCollectionGeneratorWindow : EditorWindow
{
    [MenuItem("Multiplayer/CodeGen/PipelineCollection Generator")]
    public static void ShowWindow()
    {
        GetWindow<PipelineCollectionGeneratorWindow>(false, "PipelineCollection Generator", true);
    }

    class PipelineStage
    {
        public Type type;
        public bool generate;
    }
    private List<PipelineStage> m_PipelineTypes;
    public PipelineCollectionGeneratorWindow()
    {
        m_PipelineTypes = new List<PipelineStage>();
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Scan for Pipelines"))
        {
            FindAllPipelines();
        }

        for (int i = 0; i < m_PipelineTypes.Count; ++i)
        {
            m_PipelineTypes[i].generate = GUILayout.Toggle(m_PipelineTypes[i].generate, m_PipelineTypes[i].type.Name);
        }

        if (GUILayout.Button("Generate Collection"))
        {
            var dstFile = EditorUtility.SaveFilePanel("Select file to save", "", "PipelineStageCollection", "cs");

            var content = @"using System;
using Unity.Collections;
using Unity.Networking.Transport;
public struct PipelineStageCollection : INetworkPipelineStageCollection
{
";
            for (int i = 0; i < m_PipelineTypes.Count; ++i)
            {
                if (m_PipelineTypes[i].generate)
                    content += "    private " + m_PipelineTypes[i].type.Name + " m_" + m_PipelineTypes[i].type.Name + ";\n";
            }

            content += "    public int GetStageId(Type type)\n    {\n";
            int stageIdx = 0;
            for (int i = 0; i < m_PipelineTypes.Count; ++i)
            {
                if (m_PipelineTypes[i].generate)
                {
                    content += "        if (type == typeof(" + m_PipelineTypes[i].type.Name + "))\n            return " +
                               stageIdx + ";\n";
                    ++stageIdx;
                }
            }

            content += "        return -1;\n    }\n";

            content += @"    public void Initialize(params INetworkParameter[] param)
    {
        for (int i = 0; i < param.Length; ++i)
        {
";
            for (int i = 0; i < m_PipelineTypes.Count; ++i)
            {
                if (m_PipelineTypes[i].generate)
                {
                    var inits = m_PipelineTypes[i].type.GetCustomAttributes(typeof(NetworkPipelineInitilizeAttribute), true);
                    foreach (var init in inits)
                    {
                        var pipelineInit = init as NetworkPipelineInitilizeAttribute;
                        content += "            if (param[i] is " + pipelineInit.ParameterType.FullName.Replace("+", ".") + ")\n";
                        content += "                m_" + m_PipelineTypes[i].type.Name + ".Initialize((" + pipelineInit.ParameterType.FullName.Replace("+", ".") + ")param[i]);\n";
                    }
                }
            }
            content += "        }\n    }\n";

            content += GenerateVoidInvoke(
                "void InvokeInitialize(int pipelineStageId, NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedStateBuffer)",
            "InitializeConnection(sendProcessBuffer, recvProcessBuffer, sharedStateBuffer)");

            content += GenerateInvoke(
                "InboundBufferVec InvokeSend(int pipelineStageId, NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)",
                "Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate)",
                "inboundBuffer");

            content += GenerateInvoke(
                "NativeSlice<byte> InvokeReceive(int pipelineStageId, NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)",
                "Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate)",
                "inboundBuffer");

            content += GenerateInvoke(
                "int GetReceiveCapacity(int pipelineStageId)",
                "ReceiveCapacity",
                "0");

            content += GenerateInvoke(
                "int GetSendCapacity(int pipelineStageId)",
                "SendCapacity",
                "0");

            content += GenerateInvoke(
                "int GetHeaderCapacity(int pipelineStageId)",
                "HeaderCapacity",
                "0");

            content += GenerateInvoke(
                "int GetSharedStateCapacity(int pipelineStageId)",
                "SharedStateCapacity",
                "0");

            content += "}\n";
            File.WriteAllText(dstFile, content);
        }
    }

    string GenerateInvoke(string function, string perTypeInvoke, string fallback)
    {
        var content = "    public " + function + "\n    {\n";
        content += "        switch (pipelineStageId)\n        {\n";
        var stageIdx = 0;
        for (int i = 0; i < m_PipelineTypes.Count(); ++i)
        {
            if (m_PipelineTypes[i].generate)
            {
                content += "        case " + stageIdx + ":\n";
                ++stageIdx;
                content += "            return m_" + m_PipelineTypes[i].type.Name + "." + perTypeInvoke + ";\n";
            }
        }

        content += "        }\n        return " + fallback + ";\n";
        content += "    }\n";
        return content;
    }
    string GenerateVoidInvoke(string function, string perTypeInvoke)
    {
        var content = "    public " + function + "\n    {\n";
        content += "        switch (pipelineStageId)\n        {\n";
        var stageIdx = 0;
        for (int i = 0; i < m_PipelineTypes.Count(); ++i)
        {
            if (m_PipelineTypes[i].generate)
            {
                content += "        case " + stageIdx + ":\n";
                ++stageIdx;
                content += "            m_" + m_PipelineTypes[i].type.Name + "." + perTypeInvoke + ";\n            break;\n";
            }
        }

        content += "        }\n    }\n";
        return content;
    }

    void FindAllPipelines()
    {
        m_PipelineTypes.Clear();
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
                    $"PipelineCollectionGenerator failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
            }

            var pipelineTypes = allTypes.Where(t =>
                typeof(INetworkPipelineStage).IsAssignableFrom(t) &&
                !t.IsAbstract &&
                !t.ContainsGenericParameters);

            foreach (var pt in pipelineTypes)
            {
                m_PipelineTypes.Add(new PipelineStage {type = pt, generate = !pt.Name.StartsWith("Test")});
            }
        }
    }
}

