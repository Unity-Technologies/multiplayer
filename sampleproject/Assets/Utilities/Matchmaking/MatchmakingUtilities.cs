using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Ucg.Matchmaking;

public static class MatchmakingUtilities
{
    /// <summary>
    /// Creates a matchmaking request given a player ID and serializable classes containing player properties and group properties.
    /// </summary>
    /// <param name="playerId">Unique ID of the player</param>
    /// <param name="playerProps">Class containing player properties; must be serializable to JSON</param>
    /// <param name="groupProps">Class containing group (non-player) properties; must be serializable to JSON</param>
    /// /// <returns>A properly-formed matchmaking request object that can be used in calls to the matchmaking API</returns>
    public static MatchmakingRequest CreateMatchmakingRequest(string playerId, PlayerProperties playerProps, GroupProperties groupProps)
    {
        if (string.IsNullOrEmpty(playerId))
            throw new ArgumentException($"{nameof(playerId)} must be a non-null, non-0-length string", nameof(playerId));

        if (playerProps == null || !playerProps.GetType().IsSerializable)
            throw new ArgumentException($"{nameof(playerProps)} must be a non-null, serializable class or struct", nameof(playerProps));

        if (groupProps == null || !groupProps.GetType().IsSerializable)
            throw new ArgumentException($"{nameof(groupProps)} must be a non-null, serializable class or struct", nameof(groupProps));

        var playerProperties = JsonUtility.ToJson(playerProps);
        var groupProperties = JsonUtility.ToJson(groupProps);

        return CreateMatchmakingRequest(playerId, playerProperties, groupProperties);
    }

    /// <summary>
    /// Generate a Match Request object using pre-serialized player and group properties.  Does not check for valid JSON.
    /// </summary>
    /// <param name="playerId">Unique ID of the player</param>
    /// <param name="serializedPlayerProps">Pre-serialized player properties</param>
    /// <param name="serializedGroupProps">Pre-serialized group properties</param>
    /// <returns>A properly-formed matchmaking request object that can be used in calls to the matchmaking API</returns>
    public static MatchmakingRequest CreateMatchmakingRequest(string playerId, string serializedPlayerProps, string serializedGroupProps)
    {
        if (string.IsNullOrEmpty(playerId))
            throw new ArgumentException($"{nameof(playerId)} must be a non-null, non-0-length string", nameof(playerId));

        if (string.IsNullOrEmpty(serializedPlayerProps))
            throw new ArgumentException($"{nameof(serializedPlayerProps)} must be a non-null, non-0-length string", nameof(serializedPlayerProps));

        if (string.IsNullOrEmpty(serializedGroupProps))
            throw new ArgumentException($"{nameof(serializedGroupProps)} must be a non-null, non-0-length string", nameof(serializedGroupProps));

        var thisPlayer = new MatchmakingPlayer(playerId, serializedPlayerProps);
        var players = new List<MatchmakingPlayer>() { thisPlayer };
        var request = new MatchmakingRequest(players, serializedGroupProps);

        return request;
    }

    /// <summary>
    /// This is an example of custom player properties
    /// A [Serializable] class containing fields that represent the player's properties
    /// /// [Serializable] is required to allow JsonUtility to properly convert the class to JSON
    /// </summary>
    [Serializable]
    public class PlayerProperties
    {
        public int hats;
    }

    /// <summary>
    /// This is an example of custom match request properties
    /// A [Serializable] class containing fields that represent the group's (non-player) properties
    /// [Serializable] is required to allow JsonUtility to properly convert the class to JSON
    /// </summary>
    [Serializable]
    public class GroupProperties
    {
        public int mode;
    }
}
