using System;
using System.Numerics;

namespace CoordinatedPolice;

internal static class SearchPattern
{
    public static Vector3 destination(Vector3 last_known_position, int officer_index, float elapsed_seconds, int attempt)
    {
        if (officer_index < 0 || officer_index >= 128 || attempt < 0 || attempt >= 4) throw new ArgumentOutOfRangeException(nameof(officer_index));
        if (!float.IsFinite(elapsed_seconds) || !float.IsFinite(last_known_position.LengthSquared()))
            throw new ArgumentOutOfRangeException(nameof(last_known_position));
        int step = (int)(Math.Clamp(elapsed_seconds, 0f, 60f) / 6f);
        if (officer_index == 0 && step == 0 && attempt == 0) return last_known_position;
        float radius = 4f + officer_index / 16 * 3f + Math.Min(8f, step * 2f);
        float angle = (officer_index % 16 + step + attempt * 0.25f) * MathF.PI / 8f;
        return last_known_position + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
    }
}
