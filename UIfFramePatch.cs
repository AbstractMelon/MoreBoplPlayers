using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MorePlayers;

[HarmonyPatch(typeof(SteamFrame))]
[HarmonyPatch("Awake", MethodType.Normal)]
class SteamFrame_Awake_Patch
{
    static void Prefix(SteamFrame __instance)
    {
        if (__instance.squares.Count == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                var newButton = GameObject.Instantiate(__instance.squares[i]);
                newButton.transform.SetParent(__instance.squares[i].transform.parent, false);
                __instance.squares.Add(newButton);
            }
        }
    }
}

[HarmonyPatch(typeof(SteamFrame))]
[HarmonyPatch("Update", MethodType.Normal)]
class SteamFrame_Update_Patch
{
    static void Postfix(SteamFrame __instance)
    {
        __instance.nrOfSquares = Mathf.Min(__instance.squares.Count, 8);

        for (int i = 0; i < __instance.squares.Count; i++)
        {
            __instance.squares[i].gameObject.SetActive(i < __instance.nrOfSquares);
        }
    }
}