using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers
{
    [BepInPlugin("com.MorePlayersTeam.MorePlayers", "MorePlayers", "1.0.0")]
    public class Main : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony harmony;
        private bool isVisible = true;
        private ConfigEntry<int> maxPlayers;

        private static IEnumerable<CodeInstruction> SteamManagerCreateFriendLobbyPatch(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.LoadsConstant(4))
                {
                    Log.LogMessage($"Found create lobby instruction to patch from 4 to {Constants.MAX_PLAYERS}");
                    yield return new CodeInstruction(OpCodes.Ldc_I4, Constants.MAX_PLAYERS);
                    continue;
                }

                yield return instruction;
            }
        }

        public static IEnumerable<CodeInstruction> PatchFieldLoad(FieldInfo fromA, FieldInfo fromB, FieldInfo toA,
            FieldInfo toB, IEnumerable<CodeInstruction> instructions)
        {
            bool patched = false;

            var enumerator = instructions.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var instruction = enumerator.Current;

                if (instruction.LoadsField(fromA, true))
                {
                    if (!enumerator.MoveNext())
                    {
                        Log.LogError(
                            $"Expected to find next instruction after {fromA} load instruction");
                        yield return instruction;
                    }

                    var nextInstruction = enumerator.Current;

                    if (!nextInstruction.LoadsField(fromB))
                    {
                        Log.LogInfo(
                            $"Candidate patch instruction is not loading {fromB} field: {nextInstruction.opcode}:{nextInstruction.operand}");
                        yield return instruction;
                        yield return nextInstruction;
                        continue;
                    }

                    Log.LogInfo($"Found {fromA} and {fromB} field load instructions to patch");

                    yield return new CodeInstruction(instruction.opcode, toA);
                    yield return new CodeInstruction(nextInstruction.opcode, toB);

                    patched = true;
                }
                else
                {
                    yield return instruction;
                }
            }

            if (!patched)
            {
                Log.LogError("Failed to patch GameSessionHandler.LoadNextLevelScene");
            }
        }
        
        void OnGUI() // Adds a leaderboard and probably other GUI stuff in the future
        {
            var players = PlayerHandler.Get().NumberOfPlayers(); // Value
            var playerInfoList = PlayerHandler.Get().PlayerList(); // List
            GUI.color = new UnityEngine.Color(0, 0, 0, 0.5f);
            GUIStyle style = new GUIStyle(); // The style of the GUI so it looks pretty
            style.fontSize = 20;
            style.normal.textColor = Color.white;
            GUIStyle style2 = new GUIStyle(); // The style of the GUI so it looks pretty
            style2.fontSize = 20;
            style2.normal.textColor = Color.white;

            if (GUI.Button(new Rect(350, 60, 100, 30), "Toggle Visibility")) // Adds Toggle Visibility Button
            {
                if (isVisible == false)
                {
                    isVisible = true;
                } else if (isVisible == true)
                {
                    isVisible = false;
                }
            }

            for (int i = 0; i < playerInfoList.Count; i++) // Does the leaderboard stuff
            {
                if (isVisible == false)
                {
                    return;
                }
                else

                {
                    string userColor = playerInfoList[i].Color.ToString().Replace("Slime (UnityEngine.Material)", "");
                    string fixedUserColor = char.ToUpper(userColor[0]) + userColor.Substring(1);
                    float yPosition = 130 + i * 25;

                    GUI.Label(new Rect(25, yPosition, 300, 30), $"Player {fixedUserColor} ({i + 1}): Kills: {playerInfoList[i].Kills}, Deaths: {playerInfoList[i].Deaths}, Cause of Death: {playerInfoList[i].CauseOfDeath}", style);
                }

            }



            if (isVisible)
            {
                GUI.Label(new Rect(25, 100, 300, 30), $"MoreBopl is running.. Currently: {players} players", style2);
                GUI.DrawTexture(new Rect(0, 100, 650, 25 + playerInfoList.Count * 25), Texture2D.whiteTexture);
            }

            


        }

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Logger Loaded");

            // Configuration
            maxPlayers = Config.Bind("General", "MaxPlayers", 8, "The maximum number of players allowed in a lobby.");
            Constants.MAX_PLAYERS = maxPlayers.Value;


            Host.recordReplay = false; // Disable replay recording since I'm lazy to implement it
            Logger.LogInfo("Disabled replay recording");

            harmony = new Harmony("com.MorePlayersTeam.MorePlayers");

            harmony.PatchAll();

            var targetMethod =
                typeof(SteamManager).GetMethod("CreateFriendLobby", BindingFlags.Instance | BindingFlags.Public);
            if (targetMethod == null)
            {
                Logger.LogError("Failed to find SteamManager::CreateFriendLobby!");
                return;
            }

            var stateMachineAttr = targetMethod.GetCustomAttribute<AsyncStateMachineAttribute>();
            var moveNextMethod =
                stateMachineAttr.StateMachineType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
            var startTranspiler = typeof(Main).GetMethod(nameof(SteamManagerCreateFriendLobbyPatch),
                BindingFlags.Static | BindingFlags.NonPublic);

            var patcher = harmony.CreateProcessor(moveNextMethod);
            patcher.AddTranspiler(startTranspiler);
            patcher.Patch();

            Logger.LogInfo($"More players acquired! Max players: {Constants.MAX_PLAYERS}");
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(printText))]
    [HarmonyPatch("Awake")]
    public static class PatchVersion
    {
        public static void Prefix()
        {
            Main.Log.LogInfo($"Found version {Constants.version}");
            Constants.version += "-More Players Modded";
            Main.Log.LogInfo($"Patched to version {Constants.version}");
        }
    }
}
