using System;

namespace CoordinatedPolice;

internal sealed record PoliceSettings
{
    public static PoliceSettings current { get; set; } = new();
    public int patrols_per_player { get; init; } = 5;
    public int reserve_base { get; init; } = 8;
    public int reserves_per_extra_player { get; init; } = 4;
    public float response_size { get; init; } = 1f;
    public float reinforcement_time { get; init; } = 1f;
    public int northtown_weight { get; init; } = 1;
    public int westville_weight { get; init; } = 1;
    public int downtown_weight { get; init; } = 1;
    public int docks_weight { get; init; } = 1;
    public int suburbia_weight { get; init; } = 1;
    public int uptown_weight { get; init; } = 1;

    public PoliceSettings validated()
    {
        PoliceSettings result = this with
        {
            patrols_per_player = Math.Clamp(patrols_per_player, 0, 16),
            reserve_base = Math.Clamp(reserve_base, 0, 64),
            reserves_per_extra_player = Math.Clamp(reserves_per_extra_player, 0, 16),
            response_size = float.IsFinite(response_size) ? Math.Clamp(response_size, 0.25f, 2f) : 1f,
            reinforcement_time = float.IsFinite(reinforcement_time) ? Math.Clamp(reinforcement_time, 0.25f, 4f) : 1f,
            northtown_weight = Math.Clamp(northtown_weight, 0, 10),
            westville_weight = Math.Clamp(westville_weight, 0, 10),
            downtown_weight = Math.Clamp(downtown_weight, 0, 10),
            docks_weight = Math.Clamp(docks_weight, 0, 10),
            suburbia_weight = Math.Clamp(suburbia_weight, 0, 10),
            uptown_weight = Math.Clamp(uptown_weight, 0, 10)
        };
        return result;
    }

    public int weight(int district) => district switch
    {
        0 => northtown_weight,
        1 => westville_weight,
        2 => downtown_weight,
        3 => docks_weight,
        4 => suburbia_weight,
        5 => uptown_weight,
        _ => throw new ArgumentOutOfRangeException(nameof(district))
    };
}
