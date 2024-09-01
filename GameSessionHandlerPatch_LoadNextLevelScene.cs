using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BoplFixedMath;
using HarmonyLib;

namespace MorePlayers;

[HarmonyPatch(typeof(GameSessionHandler))]
[HarmonyPatch(nameof(GameSessionHandler.LoadNextLevelScene))]
public class GameSessionHandlerPatch_LoadNextLevelScene
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Main.Log.LogInfo("Patching GameSessionHandler.LoadNextLevelScene");
        FieldInfo startParametersFieldInfo =
            AccessTools.Field(typeof(SteamManager), nameof(SteamManager.startParameters));
        FieldInfo multiStartParametersFieldInfo =
            AccessTools.Field(typeof(SteamManagerExtended), nameof(SteamManagerExtended.startParameters));
        FieldInfo seedFieldInfo = AccessTools.Field(typeof(StartRequestPacket), nameof(StartRequestPacket.seed));
        FieldInfo multiSeedFieldInfo =
            AccessTools.Field(typeof(MultiStartRequestPacket), nameof(MultiStartRequestPacket.seed));

        return Main.PatchFieldLoad(startParametersFieldInfo, seedFieldInfo, multiStartParametersFieldInfo,
            multiSeedFieldInfo, instructions);
    }
}

[HarmonyPatch(typeof(GameSessionHandler))]
[HarmonyPatch("SpawnPlayers")]
public class GameSessionHandlerPatch_SpawnPlayers
{
    static void Prefix()
    {
        var players = PlayerHandler.Get().PlayerList();
        Main.Log.LogInfo($"Spawning {players.Count} players:");
        foreach (var player in players)
        {
            Main.Log.LogInfo($"Player {player.Id} ({player.steamId})");
        }
    }
}