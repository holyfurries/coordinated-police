using System;
using System.Numerics;

namespace CoordinatedPolice;

internal static class PursuitSpacing
{
    public static Vector2 separation(Vector2 relative, int officer_index, int neighbor_index)
    {
        float distance = relative.Length();
        if (!float.IsFinite(distance) || distance >= 3f || officer_index < 0 || neighbor_index < 0 || officer_index == neighbor_index)
            return Vector2.Zero;
        if (distance < 0.01f)
        {
            float angle = Math.Min(officer_index, neighbor_index) % 8 * MathF.PI / 4f;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (officer_index < neighbor_index ? -3f : 3f);
        }
        return relative / distance * (3f - distance);
    }

    public static Vector2 lateral_offset(Vector2 separation, Vector2 toward_target)
    {
        if (!float.IsFinite(separation.LengthSquared()) || !float.IsFinite(toward_target.LengthSquared())) return Vector2.Zero;
        float distance = toward_target.Length();
        if (distance < 2f) return Vector2.Zero;
        Vector2 direction = toward_target / distance;
        Vector2 lateral = separation - direction * Vector2.Dot(separation, direction);
        float length = lateral.Length();
        return length > 2.5f ? lateral * (2.5f / length) : lateral;
    }
}
