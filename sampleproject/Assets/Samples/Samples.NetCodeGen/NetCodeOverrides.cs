using System.Collections.Generic;
using Unity.NetCode.Editor;

namespace Unity.NetCode.Samples
{
    public class NetCodeOverrides : IGhostDefaultOverridesModifier
    {
        public void Modify(Dictionary<string, GhostComponentModifier> overrides)
        {
        }

        public void ModifyAlwaysIncludedAssembly(HashSet<string> alwaysIncludedAssemblies)
        {
        }

        public void ModifyTypeRegistry(TypeRegistry typeRegistry, string netCodeGenAssemblyPath)
        {
            typeRegistry.Templates.Add(
                new TypeDescription(typeof(Mathematics.float3),
                    TypeAttribute.Specialized(
                        TypeAttribute.AttributeFlags.Quantized | TypeAttribute.AttributeFlags.Interpolated, Asteroids.Mixed.SubTypes.Float3_XY)),
                new TypeTemplate
                {
                    SupportsQuantization = true,
                    Composite = false,
                    SupportCommand = false,
                    TemplatePath = "Assets/Samples/Samples.NetCodeGen/Templates/Translation2d.cs",
                    TemplateOverridePath = null
                });
            typeRegistry.Templates.Add(
                new TypeDescription(typeof(Mathematics.quaternion),
                    TypeAttribute.Specialized(
                        TypeAttribute.AttributeFlags.Quantized | TypeAttribute.AttributeFlags.Interpolated, Asteroids.Mixed.SubTypes.Rotation2D)),
                new TypeTemplate
                {
                    SupportsQuantization = true,
                    Composite = false,
                    SupportCommand = false,
                    TemplatePath = "Assets/Samples/Samples.NetCodeGen/Templates/Rotation2d.cs",
                    TemplateOverridePath = null
                });
        }
    }
}