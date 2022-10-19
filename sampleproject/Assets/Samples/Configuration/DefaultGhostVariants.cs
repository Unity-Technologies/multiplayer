using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;

namespace Unity.NetCode.Samples
{
    /// <summary>Registers the default variants for all samples. Since multiple user-defined variants are present for the
    /// Transform components, we must explicitly define a default, and how it applies to components on child entities.</summary>
    sealed class DefaultVariantSystem : DefaultVariantSystemBase
    {
        protected override void RegisterDefaultVariants(Dictionary<ComponentType, Rule> defaultVariants)
        {
            defaultVariants.Add(typeof(Rotation), Rule.OnlyParents(typeof(RotationDefaultVariant)));
            defaultVariants.Add(typeof(Translation), Rule.OnlyParents(typeof(TranslationDefaultVariant)));
        }
    }
}
