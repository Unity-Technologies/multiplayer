using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Rendering;
using UnityEngine;

[UpdateInGroup(typeof(GhostInputSystemGroup))]
[AlwaysSynchronizeSystem]
public partial class SetPlayerColor : SystemBase
{
    protected override void OnUpdate()
    {
        FixedString32Bytes worldName = World.Name;
        Entities.WithChangeFilter<RenderMesh>().ForEach((Entity ent, ref URPMaterialPropertyBaseColor color, in GhostOwnerComponent ghostOwner) =>
        {
            color.Value = GetColorForNetworkId(ghostOwner.NetworkId);
            Debug.Log($"'{worldName}' setting color for NetworkId '{ghostOwner.NetworkId}' to '{color.Value}'!");
        }).Run();
    }

    public static Vector4 GetColorForNetworkId(int networkId) => networkId switch
    {
        1 => Color.red,
        2 => new Color(0.07f, 0f, 1f),
        3 => Color.green,
        4 => Color.yellow,
        5 => Color.magenta,
        6 => Color.black,
        7 => new Color(0.84f, 0.84f, 0.84f),
        8 => new Color(1f, 0.54f, 0f),
        9 => Color.cyan,
        10 => new Color(0.46f, 0f, 0.72f),
        _ => Color.grey
    };
}
