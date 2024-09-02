using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace MorePlayers
{
    internal class SteamManagerExtended
    {
        public static MultiStartRequestPacket startParameters;
    }

    [HarmonyPatch(typeof(SteamManager))]
    [HarmonyPatch("HostGame")]
    static class SteamManagerPatch_HostGame
    {
        static AccessTools.FieldRef<SteamManager, ushort> nextStartGameSeqAccess =
            AccessTools.FieldRefAccess<SteamManager, ushort>("nextStartGameSeq");

        // ReSharper disable once UnusedMember.Global
        public static bool Prefix(SteamManager __instance, PlayerInit hostPlayer)
        {
            Main.Log.LogInfo("Running custom HostGame patch");

            __instance.currentLobby.SetData("LFM", "0");
            __instance.currentLobby.SetFriendsOnly();
            __instance.currentLobby.SetJoinable(b: false);
            SteamManagerExtended.startParameters = default;
            SteamManagerExtended.startParameters.seqNum = nextStartGameSeqAccess(__instance)++;
            SteamManagerExtended.startParameters.nrOfPlayers = (byte)(__instance.connectedPlayers.Count + 1);
            SteamManagerExtended.startParameters.nrOfAbilites = (byte)Settings.Get().NumberOfAbilities;
            SteamManagerExtended.startParameters.currentLevel = GameSession.CurrentLevel();
            SteamManagerExtended.startParameters.seed = (uint)Environment.TickCount;

            SteamManagerExtended.startParameters.Initialize(__instance.connectedPlayers.Count + 1);

            Main.Log.LogInfo("My SteamID: " + SteamClient.SteamId);
            SteamManagerExtended.startParameters.p_ids[0] = SteamClient.SteamId;
            SteamManagerExtended.startParameters.p_teams[0] = (byte)hostPlayer.team;
            SteamManagerExtended.startParameters.p_colors[0] = (byte)hostPlayer.color;
            SteamManagerExtended.startParameters.p_ability1s[0] = (byte)hostPlayer.ability0;
            SteamManagerExtended.startParameters.p_ability2s[0] = (byte)hostPlayer.ability1;
            SteamManagerExtended.startParameters.p_ability3s[0] = (byte)hostPlayer.ability2;

            for (int i = 1; i < SteamManagerExtended.startParameters.nrOfPlayers; i++)
            {
                Main.Log.LogInfo($"{i} <? {__instance.connectedPlayers.Count}");
                SteamManagerExtended.startParameters.p_ids[i] = __instance.connectedPlayers[i - 1].id;
                SteamManagerExtended.startParameters.p_teams[i] = (byte)__instance.connectedPlayers[i - 1].lobby_team;
                SteamManagerExtended.startParameters.p_colors[i] = (byte)__instance.connectedPlayers[i - 1].lobby_color;
                SteamManagerExtended.startParameters.p_ability1s[i] = __instance.connectedPlayers[i - 1].lobby_ability1;
                SteamManagerExtended.startParameters.p_ability2s[i] = __instance.connectedPlayers[i - 1].lobby_ability2;
                SteamManagerExtended.startParameters.p_ability3s[i] = __instance.connectedPlayers[i - 1].lobby_ability3;
            }

            byte b = (byte)(SteamManager.instance.dlc.HasDLC() ? 1u : 0u);
            for (int i = 0; i < __instance.connectedPlayers.Count; i++)
            {
                if (__instance.connectedPlayers[i].ownsFullGame)
                {
                    b = (byte)(b | (1 << i + 1));
                }
            }

            SteamManagerExtended.startParameters.isDemoMask = b;

            byte[] startRequestBuffer =
                new byte[NetworkToolsExtensions.GetMultiStartRequestSize(SteamManagerExtended.startParameters)];

            Main.Log.LogInfo(SteamManagerExtended.startParameters.ToString());
            NetworkToolsExtensions.EncodeMultiStartRequest(ref startRequestBuffer,
                SteamManagerExtended.startParameters);
            // instance.EncodeCurrentStartParameters_forReplay(ref instance.networkClient.EncodedStartRequest,
            //     startParameters);
            for (int j = 0; j < __instance.connectedPlayers.Count; j++)
            {
                __instance.connectedPlayers[j].Connection.SendMessage(startRequestBuffer);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(SteamManager))]
    [HarmonyPatch("ForceLoadNextLevel")]
    static class SteamManagerPatch_ForceLoadNextLevel
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(SteamManager), "ChangePlayerAbilites")]
        public static void ChangePlayerAbilities(Player player, byte ability1, byte ability2,
            byte ability3,
            int nrOfAbilities, NamedSpriteList abilityIcons) =>
            // its a stub so it has no initial content
            throw new NotImplementedException("It's a stub");

        // TODO: This only changes part of the method, look into using a transpiler to only patch this part instead of overriding the whole method for better compatibility with other mods
        // ReSharper disable once UnusedMember.Global
        public static bool Prefix()
        {
            Main.Log.LogInfo("Running custom ForceLoadNextLevel patch");

            SteamManager.networkClientHandle.DeInit();
            MultiStartRequestPacket startRequestPacket = SteamManagerExtended.startParameters;
            List<Player> list = PlayerHandler.Get().PlayerList();

            if (startRequestPacket.nrOfPlayers <= 1)
            {
                GameSessionHandler.LeaveGame(abandonLobbyEntirely: true);
                return false;
            }

            for (int num = list.Count - 1; num >= 0; num--)
            {
                if (list[num].IsLocalPlayer) continue;

                bool isConnected = SteamManager.instance.connectedPlayers.Any(player => player.id == list[num].steamId);
                if (isConnected) continue;

                list.RemoveAt(num);
                Debug.Log("dropping player because they were disconnected");
            }

            for (int j = 0; j < list.Count; j++)
            {
                ChangePlayerAbilities(list[j], startRequestPacket.p_ability1s[j],
                    startRequestPacket.p_ability2s[j], startRequestPacket.p_ability3s[j],
                    startRequestPacket.nrOfAbilites, SteamManager.instance.abilityIcons);
            }

            GameSession.SetCurrentLevel(startRequestPacket.currentLevel);
            if (!GameSessionHandler.GameIsPaused)
            {
                CharacterStatsList.ForceNextLevelImmediately();
            }
            else
            {
                CharacterStatsList.ForceNextLevelLoad();
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(SteamManager))]
    [HarmonyPatch("InitNetworkClient")]
    static class SteamManagerPatch_InitNetworkClient
    {
        static bool Prefix(SteamManager __instance)
        {
            Main.Log.LogInfo("Running custom InitNetworkClient patch");

            int localPlayerId = Array.IndexOf(SteamManagerExtended.startParameters.p_ids, SteamClient.SteamId.Value) +
                                1;

            List<Client> list = new List<Client>();
            foreach (var steamConnection in __instance.connectedPlayers)
            {
                var id = steamConnection.id.Value;

                int playerId = Array.IndexOf(SteamManagerExtended.startParameters.p_ids, id) + 1;
                list.Add(new Client(playerId, steamConnection));
            }

            __instance.networkClient.Init(list, localPlayerId, SteamManagerExtended.startParameters.currentLevel,
                __instance.startFrameBuffer, __instance.checkForDesyncs);
            SteamManager.networkClientHandle = __instance.networkClient;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamManager))]
    [HarmonyPatch("HostNextLevel")]
    static class SteamManagerPatch_HostNextLevel
    {
        static bool Prefix(SteamManager __instance, Player hostPlayer, NamedSpriteList abilityIcons)
        {
            Main.Log.LogInfo("Running custom HostNextLevel patch");

            GameSession.CurrentLevel();
            SteamManagerExtended.startParameters.frameBufferSize = (byte)Host.CurrentDelayBufferSize;
            SteamManagerExtended.startParameters.seed = (uint)Environment.TickCount;
            SteamManagerExtended.startParameters.nrOfPlayers = (byte)(__instance.connectedPlayers.Count + 1);
            SteamManagerExtended.startParameters.currentLevel = GameSession.CurrentLevel();
            SteamManagerExtended.startParameters.p_ability1s[0] =
                (byte)abilityIcons.IndexOf(hostPlayer.Abilities[0].name);
            if (Settings.Get().NumberOfAbilities > 1)
            {
                SteamManagerExtended.startParameters.p_ability2s[0] =
                    (byte)abilityIcons.IndexOf(hostPlayer.Abilities[1].name);
            }

            if (Settings.Get().NumberOfAbilities > 2)
            {
                SteamManagerExtended.startParameters.p_ability3s[0] =
                    (byte)abilityIcons.IndexOf(hostPlayer.Abilities[2].name);
            }

            for (int i = 0; i < __instance.connectedPlayers.Count; i++)
            {
                SteamManagerExtended.startParameters.p_ability1s[i + 1] = __instance.connectedPlayers[i].lobby_ability1;
                SteamManagerExtended.startParameters.p_ability2s[i + 1] = __instance.connectedPlayers[i].lobby_ability2;
                SteamManagerExtended.startParameters.p_ability3s[i + 1] = __instance.connectedPlayers[i].lobby_ability3;
            }

            byte[] startRequestBuffer =
                new byte[NetworkToolsExtensions.GetMultiStartRequestSize(SteamManagerExtended.startParameters)];
            NetworkToolsExtensions.EncodeMultiStartRequest(ref startRequestBuffer, SteamManagerExtended.startParameters);
            // SteamManager.instance.EncodeCurrentStartParameters_forReplay(ref SteamManager.instance.networkClient.EncodedStartRequest,
            //     startParameters);
            for (int i = 0; i < __instance.connectedPlayers.Count; i++)
            {
                __instance.connectedPlayers[i].Connection.SendMessage(startRequestBuffer);
            }

            return false;
        }
        [HarmonyPatch(typeof(SteamManager))]
        [HarmonyPatch("KickPlayer")]
        static class SteamManagerPatch_KickPlayer
        {
            static void Prefix(ref int connectedPlayerIndex, SteamManager __instance)
            {
                connectedPlayerIndex = Mathf.Clamp(connectedPlayerIndex, 0, __instance.connectedPlayers.Count - 1);
            }
        }
    }
}
