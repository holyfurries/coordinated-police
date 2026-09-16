using System;
using System.Numerics;

namespace CoordinatedPolice;

internal static class Interception
{
    public static bool try_offset(Vector2 velocity, Vector2 officer_offset, int flank_index, out Vector2 offset)
    {
        offset = default;
        if (flank_index < 0 || flank_index >= 64) return false;
        float speed = velocity.Length();
        if (!float.IsFinite(speed) || speed < 0.75f || speed > 12f) return false;
        if (!float.IsFinite(officer_offset.LengthSquared())) return false;
        Vector2 direction = velocity / speed;
        Vector2 side = new(direction.Y, -direction.X);
        side *= flank_index % 2 == 0 ? 1f : -1f;
        int lane = flank_index / 2;
        float width = 6f + lane % 4 * 2f;
        float depth = lane / 4 * 2f;
        float lead = Math.Clamp(speed * 1.25f, 2f, 8f);
        if (Vector2.Dot(officer_offset, side) < width / 2f)
            lead = Math.Clamp(Vector2.Dot(officer_offset, direction) + 3f, -8f, -2f);
        offset = direction * (lead - depth) + side * width;
        return true;
    }
}
