using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;

namespace Unity.NetCode.Samples
{
    /// <summary>
    /// Register the default variants for all samples. Since multiple of those are present for Rotation/Translation
    /// </summary>
    sealed class DefaultVariantSystem : DefaultVariantSystemBase
    {
        protected override void RegisterDefaultVariants(Dictionary<ComponentType, System.Type> defaultVariants)
        {
            defaultVariants.Add(new ComponentType(typeof(Rotation)), typeof(RotationDefaultVariant));
            defaultVariants.Add(new ComponentType(typeof(Translation)), typeof(TranslationDefaultVariant));
        }
    }
}
