
using System.Collections.Generic;
namespace Unity.NetCode.Generators
{
    public static partial class UserDefinedTemplates
    {
        static partial void RegisterTemplates(List<TypeRegistryEntry> templates, string defaultRootPath)
        {
            templates.AddRange(new[]{

                new TypeRegistryEntry
                {
                    Type = "Unity.Mathematics.float3",
                    SubType = GhostFieldSubType.Translation2D,
                    Quantized = true,
                    Smoothing = SmoothingAction.InterpolateAndExtrapolate,
                    SupportCommand = false,
                    Composite = false,
                    Template = "Assets/Samples/NetCodeGen/Templates/Translation2d.cs",
                    TemplateOverride = "",
                },
                new TypeRegistryEntry
                {
                    Type = "Unity.Mathematics.quaternion",
                    SubType = GhostFieldSubType.Rotation2D,
                    Quantized = true,
                    Smoothing = SmoothingAction.InterpolateAndExtrapolate,
                    SupportCommand = false,
                    Composite = false,
                    Template = "Assets/Samples/NetCodeGen/Templates/Rotation2d.cs",
                    TemplateOverride = "",
                },
            });
        }
    }
}
