using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HarmonyLib.Tools;

namespace MorePlayers
{
    [BepInPlugin("com.MorePlayersTeam.MorePlayers", "More Players", "More Players")]
    public class Main : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony harmony;

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
                            $"Candidate patch instruction is not loading {fromB} field was: {nextInstruction.opcode}:{nextInstruction.operand}");
                        yield return instruction;
                        yield return nextInstruction;
                        continue;
                    }

                    Log.LogInfo($"Found {fromA} field load instruction to patch");
                    Log.LogInfo($"Found {fromB} field load instruction to patch");

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

        void Test()
        {
             SteamManagerExtended.startParameters = default;
            SteamManagerExtended.startParameters.seqNum = 5;
            SteamManagerExtended.startParameters.nrOfPlayers = (byte)(1 + 1);
            SteamManagerExtended.startParameters.nrOfAbilites = (byte)Settings.Get().NumberOfAbilities;
            SteamManagerExtended.startParameters.currentLevel = GameSession.CurrentLevel();
            SteamManagerExtended.startParameters.seed = (uint)Environment.TickCount;

            SteamManagerExtended.startParameters.Initialize(1);

            SteamManagerExtended.startParameters.p_ids[0] = 10;
            SteamManagerExtended.startParameters.p_teams[0] = 1;
            SteamManagerExtended.startParameters.p_colors[0] = 2;
            SteamManagerExtended.startParameters.p_ability1s[0] = 3;
            SteamManagerExtended.startParameters.p_ability2s[0] = 4;
            SteamManagerExtended.startParameters.p_ability3s[0] = 5;

            for (int i = 1; i < 2; i++)
            {
                SteamManagerExtended.startParameters.p_ids[i] = (byte)i;
                SteamManagerExtended.startParameters.p_teams[i] = (byte)i;
                SteamManagerExtended.startParameters.p_colors[i] = (byte)i;
                SteamManagerExtended.startParameters.p_ability1s[i] = 4;
                SteamManagerExtended.startParameters.p_ability2s[i] = 24;
                SteamManagerExtended.startParameters.p_ability3s[i] = 56;
            }

            byte b = (byte)(SteamManager.instance.dlc.HasDLC() ? 1u : 0u);
            for (int i = 0; i < 1; i++)
            {
                if (true)
                {
                    b = (byte)(b | (1 << i + 1));
                }
            }

            SteamManagerExtended.startParameters.isDemoMask = b;

            byte[] startRequestBuffer =
                new byte[NetworkToolsExtensions.GetMultiStartRequestSize(SteamManagerExtended.startParameters)];

            NetworkToolsExtensions.EncodeMultiStartRequest(ref startRequestBuffer,
                SteamManagerExtended.startParameters);
        }

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Logger loaded!");

            HarmonyFileLog.Enabled = true;
            Log.LogInfo(HarmonyFileLog.FileWriterPath);
            Constants.MAX_PLAYERS = 8; // TODO: This can be increased a ton possibly?

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

            Logger.LogInfo($"More bopls can now bopl!");
        }


        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(printText))]
    [HarmonyPatch("Awake")]
    public static class YourPatchClass
    {
        public static void Postfix()
        {
            Main.Log.LogInfo($"Found version {Constants.version}");
            Constants.version += "-mpmodded";
            Main.Log.LogInfo($"Patched to version {Constants.version}");
        }
    }
}