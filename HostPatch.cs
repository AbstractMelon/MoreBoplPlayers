using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using MorePlayers;

namespace MoreMultiPlayer;

public class HostPatch
{
    public static Queue<MultiInputPacket> InputBuffer = new();
    public static MultiInputPacket previousInputPacket;

    public static uint GetMaxSeqNumber(MultiInputPacket packet)
    {
        return packet.inputPackets.Count == 0 ? 0 : packet.inputPackets.Max(p => p.Value.seqNumber);
    }

    public static int GetMaxPreviousTargetDelayBufferSize()
    {
        return previousInputPacket.inputPackets.Count == 0 ? 0 : previousInputPacket.inputPackets.Max(p => p.Value.targetDelayBufferSize);
    }
}

[HarmonyPatch(typeof(Host))]
[HarmonyPatch("Start")]
public class HostPatch_Start
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Main.Log.LogInfo("Patching Host.Start");
        FieldInfo startParametersFieldInfo =
            AccessTools.Field(typeof(SteamManager), nameof(SteamManager.startParameters));
        FieldInfo multiStartParametersFieldInfo =
            AccessTools.Field(typeof(SteamManagerExtended), nameof(SteamManagerExtended.startParameters));
        FieldInfo abilityFieldInfo =
            AccessTools.Field(typeof(StartRequestPacket), nameof(StartRequestPacket.nrOfAbilites));
        FieldInfo multiAbilityFieldInfo =
            AccessTools.Field(typeof(MultiStartRequestPacket), nameof(MultiStartRequestPacket.nrOfAbilites));

        return Main.PatchFieldLoad(startParametersFieldInfo, abilityFieldInfo, multiStartParametersFieldInfo,
            multiAbilityFieldInfo, instructions);
    }
}

[HarmonyPatch(typeof(Host))]
[HarmonyPatch("ReInit")]
public class HostPatch_ReInit
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Main.Log.LogInfo("Patching Host.ReInit");
        FieldInfo startParametersFieldInfo =
            AccessTools.Field(typeof(SteamManager), nameof(SteamManager.startParameters));
        FieldInfo multiStartParametersFieldInfo =
            AccessTools.Field(typeof(SteamManagerExtended), nameof(SteamManagerExtended.startParameters));
        FieldInfo seedFieldInfo =
            AccessTools.Field(typeof(StartRequestPacket), nameof(StartRequestPacket.frameBufferSize));
        FieldInfo multiSeedFieldInfo =
            AccessTools.Field(typeof(MultiStartRequestPacket), nameof(MultiStartRequestPacket.frameBufferSize));

        return Main.PatchFieldLoad(startParametersFieldInfo, seedFieldInfo, multiStartParametersFieldInfo,
            multiSeedFieldInfo, instructions);
    }
}

[HarmonyPatch(typeof(Host))]
[HarmonyPatch("Init")]
public class HostPatch_Init
{
    public static void Prefix()
    {
        Main.Log.LogInfo("Host::Init::Prefix");
        HostPatch.previousInputPacket = new MultiInputPacket();
    }
}

[HarmonyPatch(typeof(Host))]
[HarmonyPatch("ProcessNetworkPackets")]
public class HostPatch_ProcessNetworkPackets
{
    static void AddDefaultPacket()
    {
        Main.Log.LogInfo("Adding default packet");
        HostPatch.InputBuffer.Enqueue(new MultiInputPacket());
    }

    static void AddInputPacket(List<Client> clients, Queue<InputPacket> sentPacketHistory, int localPlayerId)
    {
        MultiInputPacket item = new MultiInputPacket();
        foreach (var client in clients)
        {
            InputPacket inputPacket = client.inputHistory.Dequeue();
            item.inputPackets.Add(client.PlayerId, inputPacket);
        }

        item.inputPackets.Add(localPlayerId, sentPacketHistory.Dequeue());

        HostPatch.InputBuffer.Enqueue(item);
        // UpdatesPlacedInDelayBuffer++; Done in IL code
    }

    static IEnumerable<CodeInstruction> DebugLogInstructions(string message)
    {
        yield return new CodeInstruction(OpCodes.Ldstr, message);
        yield return new CodeInstruction(OpCodes.Call,
            AccessTools.Method(typeof(HostPatch_ProcessNetworkPackets), nameof(LogText)));
    }

    static void LogText(string message)
    {
        Main.Log.LogInfo(message);
    }

    [HarmonyDebug]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        ILGenerator generator)
    {
        Main.Log.LogInfo("Patching Host.ProcessNetworkPackets");

        FieldInfo delayBufferSizeFieldInfo = AccessTools.Field(typeof(Host), "delayBufferSize");
        FieldInfo updatesPlacedInDelayBufferFieldInfo = AccessTools.Field(typeof(Host), "UpdatesPlacedInDelayBuffer");

        var enumerator = instructions.GetEnumerator();
        Label? last_br = null;
        while (enumerator.MoveNext())
        {
            var instruction = enumerator.Current;

            if (instruction.Branches(out Label? blabel))
            {
                last_br = blabel;
            }

            if (instruction.opcode == OpCodes.Ldloca_S)
            {
                var loopStart = instruction;
                var labels = loopStart.ExtractLabels();
                var instructionsSkipped = new List<CodeInstruction>();
                instructionsSkipped.Add(loopStart);

                if (labels.Count == 0)
                {
                    yield return loopStart;
                    continue;
                }

                if (!enumerator.MoveNext())
                {
                    Main.Log.LogError("Expected to find next instruction after Ldloca_S");
                    yield return loopStart;
                    yield break;
                }

                var nextInstruction = enumerator.Current;
                instructionsSkipped.Add(nextInstruction);

                if (nextInstruction.Is(OpCodes.Initobj, typeof(InputPacketQuad)))
                {
                    Main.Log.LogInfo("Found possible start of loop. Searching for end...");
                    bool foundEnd = false;

                    while (enumerator.MoveNext())
                    {
                        var instructionA = enumerator.Current;
                        instructionsSkipped.Add(instructionA);

                        if (instructionA.opcode == OpCodes.Blt && labels.Contains((Label)instructionA.operand))
                        {
                            Main.Log.LogInfo("Found End of loop");
                            foundEnd = true;
                            break;
                        }
                    }

                    if (foundEnd)
                    {
                        if (last_br == null)
                        {
                            Main.Log.LogError("What this shouldn't be null?!");
                        }
                        
                        // Check which loop was found
                        if (instructionsSkipped.Any(i => i.LoadsField(delayBufferSizeFieldInfo)))
                        {
                            Main.Log.LogInfo("Found 'fill InputBuffer with default InputPacket' loop");

                            var label = generator.DefineLabel();
                            yield return CodeInstruction.Call(typeof(HostPatch_ProcessNetworkPackets),
                                nameof(AddDefaultPacket)).WithLabels(label);

                            // Increment counter
                            yield return new CodeInstruction(OpCodes.Ldloc_3);
                            yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                            yield return new CodeInstruction(OpCodes.Add);
                            yield return new CodeInstruction(OpCodes.Stloc_3);

                            // for condition
                            yield return new CodeInstruction(OpCodes.Ldloc_3).WithLabels(last_br.Value);
                            yield return new CodeInstruction(OpCodes.Ldsfld,
                                AccessTools.Field(typeof(Host), "delayBufferSize"));
                            yield return new CodeInstruction(OpCodes.Blt_S, label);

                            continue;
                        }
                        else if (instructionsSkipped.Any(i => i.LoadsField(updatesPlacedInDelayBufferFieldInfo)))
                        {
                            var local_i = instructionsSkipped[instructionsSkipped.Count - 3].operand;
                            var local_m = instructionsSkipped[instructionsSkipped.Count - 2].operand;
                            Main.Log.LogInfo($"Local_i: {local_i}");
                            Main.Log.LogInfo($"Local_m: {local_m}");
                            
                            Main.Log.LogInfo("Found InputPacket creation loop");

                            var label = generator.DefineLabel();
                            // Prepare arguments for AddInputPacket
                            yield return new CodeInstruction(OpCodes.Ldarg_0).WithLabels(label);
                            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Host), "clients"));
                            yield return new CodeInstruction(OpCodes.Ldarg_0);
                            yield return new CodeInstruction(OpCodes.Ldfld,
                                AccessTools.Field(typeof(Host), "sentPacketHistory"));
                            yield return new CodeInstruction(OpCodes.Ldarg_0);
                            yield return new CodeInstruction(OpCodes.Ldfld,
                                AccessTools.Field(typeof(Host), "localPlayerId"));

                            // Call AddInputPacket
                            yield return CodeInstruction.Call(typeof(HostPatch_ProcessNetworkPackets),
                                nameof(AddInputPacket));

                            // Increment UpdatesPlacedInDelayBuffer
                            yield return new CodeInstruction(OpCodes.Ldarg_0);
                            yield return new CodeInstruction(OpCodes.Ldarg_0);
                            yield return new CodeInstruction(OpCodes.Ldfld,
                                AccessTools.Field(typeof(Host), "UpdatesPlacedInDelayBuffer"));
                            yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                            yield return new CodeInstruction(OpCodes.Add);
                            yield return new CodeInstruction(OpCodes.Stfld,
                                AccessTools.Field(typeof(Host), "UpdatesPlacedInDelayBuffer"));
                            
                            // Increment counter
                            yield return new CodeInstruction(OpCodes.Ldloc_S, local_i);
                            yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                            yield return new CodeInstruction(OpCodes.Add);
                            yield return new CodeInstruction(OpCodes.Stloc_S, local_i);

                            // for condition
                            yield return new CodeInstruction(OpCodes.Ldloc_S, local_i).WithLabels(last_br.Value);
                            yield return new CodeInstruction(OpCodes.Ldloc_S, local_m);
                            yield return new CodeInstruction(OpCodes.Blt_S, label);
                            
                            continue;
                        }

                        // Patch last branch to jump to the start of the new loop body
                        instructionsSkipped[instructionsSkipped.Count - 7].labels
                            .Add((Label)instructionsSkipped[instructionsSkipped.Count - 1].operand);

                        // Re-emit the last 7 instructions as they are required for the loop
                        for (int i = instructionsSkipped.Count - 7; i < instructionsSkipped.Count; i++)
                        {
                            yield return instructionsSkipped[i];
                        }

                        continue;
                    }

                    foreach (var skipped in instructionsSkipped)
                    {
                        yield return skipped;
                    }

                    yield break;
                }

                foreach (var i in instructionsSkipped)
                {
                    yield return i;
                }

                continue;
            }

            yield return instruction;
        }

        enumerator.Dispose();
    }
}

[HarmonyPatch(typeof(Host))]
[HarmonyPatch("Update")]
public class HostPatch_Update
{
    public static int GetCount()
    {
        return HostPatch.InputBuffer.Count;
    }

    public static MultiInputPacket Dequeue()
    {
        return HostPatch.InputBuffer.Dequeue();
    }

    public static int GetTargetDelayBufferSize(MultiInputPacket packet, int currentTargetDelayBufferSize)
    {
        if (HostPatch.GetMaxSeqNumber(packet) % 32 == 0)
        {
            var d = HostPatch.GetMaxPreviousTargetDelayBufferSize();
            if (d > 0)
            {
                return d;
            }
        }

        return currentTargetDelayBufferSize;
    }

    public static void OverrideInputWithNetworkInput(MultiInputPacket packet)
    {
        var playerHandler = PlayerHandler.Get();
        var playerList = playerHandler.PlayerList();
        
        for (int i = 0; i < playerList.Count; i++)
        {
            if(packet.inputPackets.TryGetValue(playerList[i].Id, out var inputPacket))
            {
                playerList[i].OverrideInputWithNetworkInput(inputPacket);
            }
        }

        HostPatch.previousInputPacket = packet;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        ILGenerator generator)
    {
        Main.Log.LogInfo("Patching Host.Update");

        FieldInfo inputBufferFieldInfo = AccessTools.Field(typeof(Host), "InputBuffer");
        MethodInfo getCountMethodInfo = AccessTools.Method(inputBufferFieldInfo.FieldType, "get_Count");
        MethodInfo dequeueMethodInfo = AccessTools.Method(inputBufferFieldInfo.FieldType, "Dequeue");

        var enumerator = instructions.GetEnumerator();
        LocalBuilder inputPacketQuadLocal = null;
        var mipl = generator.DeclareLocal(typeof(MultiInputPacket));
        bool foundInputBuffer = false;

        while (enumerator.MoveNext())
        {
            var instruction = enumerator.Current;

            if (instruction.LoadsField(inputBufferFieldInfo))
            {
                Main.Log.LogInfo("Found InputBuffer field load instruction");
                foundInputBuffer = true;

                if (!enumerator.MoveNext())
                {
                    Main.Log.LogError("Expected to find next instruction after InputBuffer field load instruction");
                    yield return instruction;
                    continue;
                }

                instruction = enumerator.Current;

                if (instruction.Calls(getCountMethodInfo))
                {
                    Main.Log.LogInfo("Patching InputBuffer.getCount");

                    // Pop the ldarg.0
                    yield return new CodeInstruction(OpCodes.Pop);

                    // Call custom GetCount
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(HostPatch_Update), nameof(GetCount)));

                    continue;
                }
                else if (instruction.Calls(dequeueMethodInfo))
                {
                    Main.Log.LogInfo("Patching InputBuffer.Dequeue");

                    // Pop the ldarg.0
                    yield return new CodeInstruction(OpCodes.Pop);

                    // Call custom Dequeue
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(HostPatch_Update), nameof(Dequeue)));


                    // Read next instruction and delete it
                    if (!enumerator.MoveNext() && enumerator.Current.opcode == OpCodes.Stloc_S)
                    {
                        Main.Log.LogError("Expected a store local instruction.");
                        yield break; // At this point there is no saving this, just break out
                    }

                    // Store the result in a local variable
                    yield return new CodeInstruction(OpCodes.Stloc, mipl);

                    // Find and patch if(AdaptiveInputDelayBuffer)

                    while (enumerator.MoveNext())
                    {
                        instruction = enumerator.Current;
                        if (instruction.opcode == OpCodes.Ldarg_0)
                        {
                            if (!enumerator.MoveNext())
                            {
                                Main.Log.LogError("Expected to find next instruction after Ldarg_0");
                                yield return instruction;
                                continue;
                            }

                            instruction = enumerator.Current;
                            if (instruction.LoadsField(AccessTools.Field(typeof(Host), "AdaptiveInputDelayBuffer")))
                            {
                                Main.Log.LogInfo("Found AdaptiveInputDelayBuffer check");
                                yield return new CodeInstruction(OpCodes.Ldarg_0);
                                yield return instruction;

                                if (!enumerator.MoveNext())
                                {
                                    Main.Log.LogError("Invalid input instruction stream.");
                                    continue;
                                }

                                instruction = enumerator.Current;
                                yield return instruction;

                                if (!instruction.Branches(out _))
                                {
                                    Main.Log.LogError(
                                        "Assumption about branch after AdaptiveInputDelayBuffer was incorrect.");
                                    continue;
                                }

                                // Inside if statement, find end of if to patch and patch getting the max seqNumber and setting the targetDelayBufferSize
                                while (enumerator.MoveNext())
                                {
                                    instruction = enumerator.Current;
                                    if (instruction.StoresField(
                                            AccessTools.Field(typeof(Host), "targetDelayBufferSize")))
                                    {
                                        break;
                                    }
                                }

                                yield return new CodeInstruction(OpCodes.Ldarg_0);
                                yield return new CodeInstruction(OpCodes.Ldloc, mipl);
                                yield return new CodeInstruction(OpCodes.Ldarg_0);
                                yield return new CodeInstruction(OpCodes.Ldfld,
                                    AccessTools.Field(typeof(Host), "targetDelayBufferSize"));
                                yield return new CodeInstruction(OpCodes.Call,
                                    AccessTools.Method(typeof(HostPatch_Update), nameof(GetTargetDelayBufferSize)));
                                yield return new CodeInstruction(OpCodes.Stfld,
                                    AccessTools.Field(typeof(Host), "targetDelayBufferSize"));

                                break;
                            }
                        }
                    }

                    continue;
                }

                Main.Log.LogError("Failed to patch InputBuffer reference out.");
            }

            if (foundInputBuffer &&
                instruction.Calls(AccessTools.Method(typeof(PlayerHandler), nameof(PlayerHandler.Get))))
            {
                Main.Log.LogInfo("Patching PlayerHandler.Get calls");

                while (enumerator.MoveNext() &&
                       !enumerator.Current.StoresField(AccessTools.Field(typeof(Host), "previousInputQuad")))
                {
                }

                if (enumerator.Current == null)
                {
                    Main.Log.LogError("Failed to find previousInputQuad store");
                    yield break;
                }

                // Patch the calls to GetPlayer
                yield return new CodeInstruction(OpCodes.Ldloc, mipl);
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(HostPatch_Update), nameof(OverrideInputWithNetworkInput)));

                continue;
            }

            yield return instruction;
        }
    }
}