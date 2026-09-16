using System;

namespace CoordinatedPolice;

internal enum ResponseSeverity { Patrol = 1, Pursuit = 2, Armed = 3, Tactical = 4 }
internal enum OfficerRole { Chaser, Interceptor, Support }
internal enum OfficerWeapon { Native, Baton, Taser, Pistol, Shotgun }

internal readonly record struct ResponseRules(int active_limit, int dispatch_limit, int burst_limit,
    float interval_seconds, float break_seconds)
{
    public static ResponseRules for_severity(ResponseSeverity severity, int player_count = 1)
    {
        ResponseRules baseline = severity switch
        {
            ResponseSeverity.Patrol => new(4, 4, 2, 8f, 30f),
            ResponseSeverity.Pursuit => new(6, 8, 2, 7f, 30f),
            ResponseSeverity.Armed => new(12, 36, 4, 2f, 8f),
            ResponseSeverity.Tactical => new(16, 64, 6, 1f, 6f),
            _ => throw new ArgumentOutOfRangeException(nameof(severity))
        };
        int extra_players = Math.Clamp(player_count, 1, 4) - 1;
        return baseline with
        {
            active_limit = baseline.active_limit + baseline.active_limit / 2 * extra_players,
            dispatch_limit = baseline.dispatch_limit + baseline.dispatch_limit / 2 * extra_players,
            burst_limit = baseline.burst_limit + extra_players + (severity >= ResponseSeverity.Armed && extra_players == 3 ? 1 : 0),
            break_seconds = severity >= ResponseSeverity.Armed ? Math.Max(4f, baseline.break_seconds - extra_players) : baseline.break_seconds
        };
    }

    public static ResponseSeverity crime_severity(string crime_name)
    {
        return crime_name switch
        {
            "DeadlyAssault" or "DischargeFirearm" or "VehicularAssault" => ResponseSeverity.Armed,
            "Assault" or "Evading" or "FailureToComply" or "BrandishingWeapon" or "DrugTrafficking" => ResponseSeverity.Pursuit,
            _ => ResponseSeverity.Patrol
        };
    }

    public static OfficerRole role(int officer_index)
    {
        if (officer_index < 0) throw new ArgumentOutOfRangeException(nameof(officer_index));
        return (officer_index % 4) switch
        {
            0 => OfficerRole.Chaser,
            2 => OfficerRole.Support,
            _ => OfficerRole.Interceptor
        };
    }

    public static OfficerWeapon weapon(ResponseSeverity severity, int pursuit_level, int officer_index)
    {
        if (officer_index < 0) throw new ArgumentOutOfRangeException(nameof(officer_index));
        if (pursuit_level == 3) return officer_index % 3 == 0 ? OfficerWeapon.Baton : OfficerWeapon.Taser;
        if (pursuit_level != 4) return OfficerWeapon.Native;
        bool shotgun = officer_index % 4 == 3 || (severity == ResponseSeverity.Tactical && officer_index % 4 == 1);
        return shotgun ? OfficerWeapon.Shotgun : OfficerWeapon.Pistol;
    }

    public static float firing_distance(OfficerRole role, OfficerWeapon weapon)
    {
        if (weapon == OfficerWeapon.Shotgun) return 4f;
        if (weapon == OfficerWeapon.Pistol) return role == OfficerRole.Support ? 8f : 5f;
        return 0f;
    }
}
