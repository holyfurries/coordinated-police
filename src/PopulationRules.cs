using System;

namespace CoordinatedPolice;

internal readonly record struct PopulationRules(int patrol_target, int reserve_target)
{
    public static PopulationRules for_players(int player_count)
    {
        int players = Math.Clamp(player_count, 1, 4);
        return new PopulationRules(players * 5, 8 + (players - 1) * 4);
    }

    public static bool can_replenish(float defeated_seconds, float now_seconds, float body_distance_squared,
        float station_distance_squared)
    {
        return float.IsFinite(defeated_seconds) && float.IsFinite(now_seconds) &&
            float.IsFinite(body_distance_squared) && float.IsFinite(station_distance_squared) &&
            now_seconds - defeated_seconds >= 45f && body_distance_squared >= 3600f && station_distance_squared >= 3600f;
    }
}
