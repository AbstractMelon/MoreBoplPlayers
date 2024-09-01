using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace MorePlayers;

[HarmonyPatch(typeof(SteamSocket))]
[HarmonyPatch("OnMessage")]
public static class SteamSocketPatch_OnMessage
{
    private static byte[] ulongConversionArray = new byte[8];

    private static byte[] uintConversionArray = new byte[4];

    private static byte[] ushortConversionArray = new byte[2];

    static CodeInstruction Log(CodeInstruction i)
    {
        Main.Log.LogInfo(i);
        return i;
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Main.Log.LogInfo("Patching SteamSocket.OnMessage()");
        
        var enumerator = instructions.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var instruction = enumerator.Current;
            
            if(instruction.Calls(AccessTools.Method(typeof(Ack), "get_PacketTypeAsEnum")))
            {
                yield return instruction;
                if (!enumerator.MoveNext())
                {
                    Main.Log.LogError("Expected to find next instruction after Ack.get_PacketTypeAsEnum() call");
                    yield break;
                }
                instruction = enumerator.Current;
                if(instruction.LoadsConstant(5)) yield return Log(new CodeInstruction(OpCodes.Ldc_I4, 255-1));
                else if (instruction.LoadsConstant(6)) yield return new CodeInstruction(OpCodes.Ldc_I4, 255);
                else yield return instruction;
                continue;
            }
            
            yield return instruction;
        }
    }
    
    static bool Prefix(SteamSocket __instance, Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
    {
        if (size >= 24 && size != 67 && size != 83)
        {
            if (!identity.SteamId.IsValid)
            {
                Main.Log.LogWarning("got message from invalid steamId");
                return false;
            }
            SteamId steamId = identity.SteamId;
            if (!new Friend(steamId).IsIn(SteamManager.instance.currentLobby.Id))
            {
                Main.Log.LogWarning("Ignored a msg from " + identity.SteamId);
                return false;
            }
            
            byte[] messageBuffer = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(data, messageBuffer, 0, size);
            
            if (SteamManager.instance.currentLobby.IsOwnedBy(identity.SteamId))
            {
                SteamManagerExtended.startParameters = NetworkToolsExtensions.ReadMultiStartRequest(messageBuffer, ref uintConversionArray, ref ulongConversionArray, ref ushortConversionArray);
                // SteamManager.instance.EncodeCurrentStartParameters_forReplay(ref SteamManager.instance.networkClient.EncodedStartRequest, SteamManager.startParameters);
                if (GameSession.inMenus)
                {
                    CharacterSelectHandler_online.ForceStartGame();
                }
                else
                {
                    SteamManager.ForceLoadNextLevel();
                }
            }
            
            return false;
        }

        return true;
    }
    
}