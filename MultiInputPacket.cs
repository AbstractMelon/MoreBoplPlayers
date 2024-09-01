using System.Collections.Generic;

namespace MorePlayers;

public struct MultiInputPacket
{
    public Dictionary<int, InputPacket> inputPackets = new();

    public MultiInputPacket()
    {
    }

    public void Log()
    {
        Main.Log.LogInfo($"MultiInputPacket: {inputPackets.Count} input packets");
        foreach (var inputPacket in inputPackets)
        {
            Main.Log.LogInfo($"Player {inputPacket.Key}:");
            Main.Log.LogInfo($"seqNumber: {inputPacket.Value.seqNumber}");
            Main.Log.LogInfo($"joystickAngle: {inputPacket.Value.joystickAngle}");
            Main.Log.LogInfo($"jump: {inputPacket.Value.jump}");
            Main.Log.LogInfo($"ab1: {inputPacket.Value.ab1}");
            Main.Log.LogInfo($"ab2: {inputPacket.Value.ab2}");
            Main.Log.LogInfo($"ab3: {inputPacket.Value.ab3}");
            Main.Log.LogInfo($"start: {inputPacket.Value.start}");
            Main.Log.LogInfo($"select: {inputPacket.Value.select}");
            Main.Log.LogInfo($"w: {inputPacket.Value.w}");
            Main.Log.LogInfo($"a: {inputPacket.Value.a}");
            Main.Log.LogInfo($"s: {inputPacket.Value.s}");
            Main.Log.LogInfo($"d: {inputPacket.Value.d}");
            Main.Log.LogInfo($"targetDelayBufferSize: {inputPacket.Value.targetDelayBufferSize}");
        }
    }
}