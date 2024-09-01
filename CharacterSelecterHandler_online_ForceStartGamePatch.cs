using HarmonyLib;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine.SceneManagement;

namespace MorePlayers
{
    [HarmonyPatch(typeof(CharacterSelectHandler_online))]
    [HarmonyPatch("ForceStartGame")]
    public static class CharacterSelecterHandler_online_ForceStartGamePatch
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(CharacterSelectHandler_online), "InitPlayer")]
        public static Player InitPlayer(int id, byte color, byte team, byte ability1, byte ability2, byte ability3,
            int nrOfAbilities, PlayerColors playerColors) =>
            // its a stub so it has no initial content
            throw new NotImplementedException("It's a stub");

        // ReSharper disable once UnusedMember.Global
        public static bool Prefix([CanBeNull] PlayerColors pcs)
        {
            var selfRef = AccessTools.StaticFieldRefAccess<CharacterSelectHandler_online, CharacterSelectHandler_online>("selfRef");
            if (pcs == null)
            {
                pcs = selfRef.playerColors;
            }
            MultiStartRequestPacket startParameters = SteamManagerExtended.startParameters;
            Updater.ReInit();

            List<Player> list = new List<Player>();
            Updater.InitSeed(startParameters.seed);
            
            Main.Log.LogInfo($"SteamID: {SteamClient.SteamId}");

            for (int i = 0; i < startParameters.nrOfPlayers; i++)
            {
                Main.Log.LogInfo($"Initializing player {i}: [id: {startParameters.p_ids[i]}, color: {startParameters.p_colors[i]}, team: {startParameters.p_teams[i]}, ability1: {startParameters.p_ability1s[i]}, ability2: {startParameters.p_ability2s[i]}, ability3: {startParameters.p_ability3s[i]}]");
                
                list.Add(InitPlayer(i + 1, startParameters.p_colors[i], startParameters.p_teams[i],
                    startParameters.p_ability1s[i], startParameters.p_ability2s[i], startParameters.p_ability3s[i],
                    startParameters.nrOfAbilites, pcs));
            }

            Player player = null;
            if (GameLobby.isPlayingAReplay)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == 1)
                    {
                        player = list[i];
                        break;
                    }
                }
            }
            else
            {
                int steamIdIndex = Array.IndexOf(startParameters.p_ids, SteamClient.SteamId);
                Main.Log.LogInfo("SteamIdIndex: " + steamIdIndex);
                if (steamIdIndex != -1)
                {
                    player = list[steamIdIndex];
                }
            }

            if (player == null)
            {
                Main.Log.LogError("Failed to find local player on ForceStart.");
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                list[i].steamId = startParameters.p_ids[i];
            }

            player.IsLocalPlayer = true;
            player.inputDevice = CharacterSelectHandler_online.localPlayerInit.inputDevice;
            player.UsesKeyboardAndMouse = CharacterSelectHandler_online.localPlayerInit.usesKeyboardMouse;
            player.CustomKeyBinding = CharacterSelectHandler_online.localPlayerInit.keybindOverride;
            CharacterSelectHandler_online.startButtonAvailable = false;
            PlayerHandler.Get().SetPlayerList(list);
            SteamManager.instance.StartHostedGame();
            AudioManager.Get()?.Play("startGame");
            GameSession.Init();
            if (GameLobby.isPlayingAReplay)
            {
                SceneManager.LoadScene(startParameters.currentLevel + 6);
            }
            else
            {
                SceneManager.LoadScene("Level1");
            }

            if (!WinnerTriangleCanvas.HasBeenSpawned)
            {
                SceneManager.LoadScene("winnerTriangle", LoadSceneMode.Additive);
            }

            return false;
        }
    }
}