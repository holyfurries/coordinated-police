using System;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.Police;
using MelonLoader;
using UnityEngine;
using GameMap = Il2CppScheduleOne.Map.Map;

namespace CoordinatedPolice;

internal static class PoliceDistricts
{
    public static readonly int[] actual = new int[6];
    public static readonly int[] targets = new int[6];
    private static readonly FootPatrolRoute?[] routes = new FootPatrolRoute?[64];
    private static readonly int[] route_districts = new int[64];
    private static readonly int[] route_members = new int[64];
    private static readonly bool[] route_available = new bool[64];
    private static readonly FootPatrolRoute?[] cached_routes = new FootPatrolRoute?[64];
    private static readonly int[] cached_districts = new int[64];
    private static int route_count;
    private static int cursor;

    public static void reset()
    {
        Array.Clear(routes, 0, routes.Length);
        Array.Clear(cached_routes, 0, cached_routes.Length);
        Array.Clear(actual, 0, actual.Length);
        Array.Clear(targets, 0, targets.Length);
        route_count = 0;
        cursor = 0;
    }

    public static bool prepare(int patrol_target)
    {
        route_count = 0;
        Array.Clear(routes, 0, routes.Length);
        Array.Clear(actual, 0, actual.Length);
        Array.Clear(targets, 0, targets.Length);
        Array.Clear(route_members, 0, route_members.Length);
        if (!GameMap.InstanceExists || !LawController.InstanceExists) return false;
        LawActivitySettings settings = LawController.Instance.GetSettings();
        if (settings == null || settings.Patrols == null) return false;
        Span<bool> eligible = stackalloc bool[6];
        eligible.Clear();
        Span<int> weights = stackalloc int[6];
        for (int i = 0; i < 6; i++) weights[i] = PoliceSettings.current.weight(i);
        int limit = Math.Min(settings.Patrols.Length, routes.Length);
        for (int i = 0; i < limit; i++)
        {
            PatrolInstance instance = settings.Patrols[i];
            if (instance == null || instance.Route == null || instance.Route.Waypoints == null ||
                instance.Route.Waypoints.Length == 0) continue;
            bool duplicate = false;
            for (int j = 0; j < route_count; j++) if (routes[j] == instance.Route) duplicate = true;
            if (duplicate) continue;
            int district = region_for_route(instance.Route);
            if (district < 0) continue;
            MapRegionData region = GameMap.Instance.GetRegionData((EMapRegion)district);
            bool available = region != null && region.IsUnlocked && weights[district] > 0;
            routes[route_count] = instance.Route;
            route_districts[route_count] = district;
            route_available[route_count++] = available;
            if (available) eligible[district] = true;
        }
        DistrictAllocation.allocate(patrol_target, eligible, weights, targets);
        int officers = Math.Min(PoliceOfficer.Officers.Count, 128);
        for (int i = 0; i < officers; i++)
        {
            PoliceOfficer officer = PoliceOfficer.Officers[i];
            if (officer == null || !officer.IsConscious || officer.isInBuilding ||
                officer.FootPatrolBehaviour == null || !officer.FootPatrolBehaviour.Active) continue;
            PatrolGroup group = officer.FootPatrolBehaviour.Group;
            FootPatrolRoute? route = group != null ? group.Route : null;
            int district = route != null ? region_for_route(route) : (int)GameMap.Instance.GetRegionFromPosition(officer.transform.position);
            if (district >= 0 && district < 6) actual[district]++;
            for (int j = 0; j < route_count; j++) if (routes[j] == route) route_members[j]++;
        }
        return true;
    }

    public static FootPatrolRoute? next_route(out int district, out int members)
    {
        int index = DistrictAllocation.choose_route(route_districts.AsSpan(0, route_count),
            route_members.AsSpan(0, route_count), route_available.AsSpan(0, route_count), actual, targets, cursor);
        district = -1;
        members = 0;
        if (index < 0) return null;
        cursor = (index + 1) % route_count;
        route_available[index] = false;
        district = route_districts[index];
        members = route_members[index];
        return routes[index];
    }

    public static int region_for_route(FootPatrolRoute route)
    {
        int empty = -1;
        for (int i = 0; i < cached_routes.Length; i++)
        {
            if (cached_routes[i] == route) return cached_districts[i];
            if (empty < 0 && cached_routes[i] == null) empty = i;
        }
        if (!GameMap.InstanceExists || route.Waypoints == null || route.Waypoints.Length == 0) return -1;
        Span<int> votes = stackalloc int[6];
        votes.Clear();
        int waypoint_count = Math.Min(route.Waypoints.Length, 128);
        int samples = Math.Min(waypoint_count, 8);
        int best = -1;
        for (int i = 0; i < samples; i++)
        {
            Transform waypoint = route.Waypoints[i * waypoint_count / samples];
            if (waypoint == null) continue;
            int district = (int)GameMap.Instance.GetRegionFromPosition(waypoint.position);
            if (district < 0 || district >= 6) continue;
            votes[district]++;
        }
        for (int i = 0; i < 6; i++) if (votes[i] > 0 && (best < 0 || votes[i] > votes[best])) best = i;
        if (empty >= 0 && best >= 0)
        {
            cached_routes[empty] = route;
            cached_districts[empty] = best;
        }
        return best;
    }

    public static void log()
    {
        MelonLogger.Msg($"Police: District patrols assigned/target Northtown={actual[0]}/{targets[0]}, Westville={actual[1]}/{targets[1]}, Downtown={actual[2]}/{targets[2]}, Docks={actual[3]}/{targets[3]}, Suburbia={actual[4]}/{targets[4]}, Uptown={actual[5]}/{targets[5]}. Targets require unlocked districts with native routes; counts follow assigned routes.");
    }
}
