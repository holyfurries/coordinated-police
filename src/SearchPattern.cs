using System;
using System.Numerics;

namespace CoordinatedPolice;

internal static class SearchPattern
{
    public static Vector3 destination(Vector3 last_known_position, int officer_index, float elapsed_seconds, int attempt)
    {
        if (officer_index < 0 || attempt < 0 || attempt >= 4) throw new ArgumentOutOfRangeException(nameof(officer_index));
        if (!float.IsFinite(elapsed_seconds) || !float.IsFinite(last_known_position.LengthSquared()))
            throw new ArgumentOutOfRangeException(nameof(last_known_position));
        int step = (int)(Math.Clamp(elapsed_seconds, 0f, 60f) / 6f);
        if (officer_index == 0 && step == 0 && attempt == 0) return last_known_position;
        float radius = Math.Min(12f, 4f + step * 2f);
        float angle = ((officer_index % 8 + step + attempt * 2) % 8) * MathF.PI / 4f;
        return last_known_position + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
    }
}
