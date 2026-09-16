using System;
using System.Numerics;

namespace CoordinatedPolice;

internal readonly record struct PursuitAssignment(OfficerRole role, Vector2 offset, bool redirect);

internal static class Interception
{
    public static PursuitAssignment assign(ReadOnlySpan<Vector2> offsets, int index, Vector2 velocity)
    {
        if (offsets.Length > 128 || index < 0 || index >= offsets.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        Vector2 relative = offsets[index];
        float distance_squared = relative.LengthSquared();
        if (!float.IsFinite(distance_squared)) return new(OfficerRole.Chaser, default, false);
        int count = 0;
        int rank = 0;
        int approach_lane = 0;
        int intercept_lane = 0;
        float speed = velocity.Length();
        bool moving = float.IsFinite(speed) && speed >= 0.75f && speed <= 12f;
        Vector2 direction = moving ? velocity / speed : Vector2.UnitX;
        Vector2 side = new(direction.Y, -direction.X);
        float lateral = Vector2.Dot(relative, side);
        bool cutoff = moving && eligible(relative, direction, side);
        int sector = approach_sector(relative);
        for (int i = 0; i < offsets.Length; i++)
        {
            float other_distance_squared = offsets[i].LengthSquared();
            if (!float.IsFinite(other_distance_squared)) continue;
            count++;
            bool closer = other_distance_squared < distance_squared || (other_distance_squared == distance_squared && i < index);
            if (!closer) continue;
            rank++;
            if (approach_sector(offsets[i]) == sector && other_distance_squared >= 144f) approach_lane++;
            if (cutoff && eligible(offsets[i], direction, side) &&
                (Vector2.Dot(offsets[i], side) < 0) == (lateral < 0)) intercept_lane++;
        }
        if (rank < (count >= 5 ? 2 : 1)) return new(OfficerRole.Chaser, default, false);
        if (distance_squared < 144f) return new(OfficerRole.Support, default, false);
        if (cutoff && intercept_lane < 4)
        {
            float lead = Math.Clamp(speed * 1.5f, 3f, 10f);
            Vector2 offset = direction * lead + side * (lateral < 0 ? -1f : 1f) * (4f + intercept_lane * 3f);
            return new(OfficerRole.Interceptor, offset, true);
        }
        if (approach_lane >= 16) return new(OfficerRole.Support, default, false);
        float angle = sector * MathF.PI / 4f + (approach_lane % 3 - 1) * MathF.PI / 12f;
        float radius = 6f + approach_lane / 3 * 3f;
        Vector2 approach = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        return new(OfficerRole.Support, approach, MathF.Sqrt(distance_squared) > radius + 3f);
    }

    private static bool eligible(Vector2 relative, Vector2 direction, Vector2 side)
    {
        if (relative.LengthSquared() < 144f) return false;
        float forward = Vector2.Dot(relative, direction);
        return forward >= 0f || (forward >= -4f && Math.Abs(Vector2.Dot(relative, side)) >= 6f);
    }

    private static int approach_sector(Vector2 relative)
    {
        int sector = (int)MathF.Round(MathF.Atan2(relative.Y, relative.X) * 4f / MathF.PI);
        return (sector + 8) % 8;
    }
}
