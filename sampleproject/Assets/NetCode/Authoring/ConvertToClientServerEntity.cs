using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

public class ConvertToClientServerEntity : ConvertToEntity
{
    [Flags]
    public enum ConversionTargetType
    {
        None = 0x0,
        Client = 0x1,
        Server = 0x2,
        ClientAndServer = 0x3
    }

    [SerializeField]
    public ConversionTargetType ConversionTarget = ConversionTargetType.ClientAndServer;

    [HideInInspector]
    public bool canDestroy = false;
    void Awake()
    {
#if !UNITY_SERVER
        bool convertToClient = (ClientServerBootstrap.clientWorld != null && ClientServerBootstrap.clientWorld.Length >= 1);
#else
        bool convertToClient = true;
#endif
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
        bool convertToServer = ClientServerBootstrap.serverWorld != null;
#else
        bool convertToServer = true;
#endif
        if (!convertToClient || !convertToServer)
        {
            UnityEngine.Debug.LogWarning("ConvertEntity failed because there was no Client and Server Worlds", this);
            return;
        }

        convertToClient &= (ConversionTarget & ConversionTargetType.Client) != 0;
        convertToServer &= (ConversionTarget & ConversionTargetType.Server) != 0;

        // Root ConvertToEntity is responsible for converting the whole hierarchy
        if (transform.parent != null && transform.parent.GetComponentInParent<ConvertToEntity>() != null)
            return;

        var defaultWorld = World.Active;
        if (ConversionMode == Mode.ConvertAndDestroy)
        {
            if (canDestroy)
            {
                ConvertHierarchy(gameObject);
            }
            else if (!convertToClient && !convertToServer)
            {
                Destroy(gameObject);
            }
            else
            {
                canDestroy = true;
#if !UNITY_SERVER
                if (convertToClient)
                {
                    int numClientsToConvert = ClientServerBootstrap.clientWorld.Length;
                    if (!convertToServer)
                        --numClientsToConvert;
                    for (int i = 0; i < numClientsToConvert; ++i)
                    {
                        World.Active = ClientServerBootstrap.clientWorld[i];
                        Instantiate(gameObject);
                    }

                    if (!convertToServer)
                        World.Active = ClientServerBootstrap.clientWorld[numClientsToConvert];
                }
#endif
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
                if (convertToServer)
                    World.Active = ClientServerBootstrap.serverWorld;
#endif
                ConvertHierarchy(gameObject);

                canDestroy = false;
            }
        }
        else
        {
#if !UNITY_SERVER
            if (convertToClient)
            {
                for (int i = 0; i < ClientServerBootstrap.clientWorld.Length; ++i)
                {
                    World.Active = ClientServerBootstrap.clientWorld[i];
                    ConvertAndInjectOriginal(gameObject);
                }
            }
#endif
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
            if (convertToServer)
            {
                World.Active = ClientServerBootstrap.serverWorld;
                ConvertAndInjectOriginal(gameObject);
            }
#endif
        }
        World.Active = defaultWorld;
    }
}
