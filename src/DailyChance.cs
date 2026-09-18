using System;

namespace CoordinatedPolice;

internal static class DailyChance
{
    public static uint score(string identity, int day)
    {
        if (identity.Length > 128 || day < 0) return uint.MaxValue;
        uint value = 2166136261;
        foreach (char letter in identity) value = unchecked((value ^ letter) * 16777619);
        value = unchecked((value ^ (uint)day) * 16777619);
        value ^= value >> 16;
        value = unchecked(value * 0x7feb352d);
        value ^= value >> 15;
        return value;
    }
}
