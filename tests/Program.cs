using System;
using System.Numerics;
using CoordinatedPolice;

internal static class Program
{
    private static void Main()
    {
        check_population();
        check_response();
        check_station_dispatch();
        check_search();
        check_severity();
        check_roles();
        check_interception();
        check_stuck_recovery();
        Console.WriteLine("Population, severity, dispatch, roles, search, interception, and recovery checks passed.");
    }

    private static void check_population()
    {
        require(PopulationRules.for_players(1).patrol_target == 5, "Solo patrol target");
        require(PopulationRules.for_players(4).patrol_target == 20, "Four-player patrol target");
        require(PopulationRules.for_players(4).reserve_target == 20, "Four-player reserves");
        require(PopulationRules.for_players(20) == PopulationRules.for_players(4), "Population scaling bounded");
        require(PopulationRules.for_players(0) == PopulationRules.for_players(1), "Minimum population target");
        require(!PopulationRules.can_replenish(0, 44, 4000, 4000), "Bodies persist for 45 seconds");
        require(!PopulationRules.can_replenish(0, 60, 3599, 4000), "Nearby player blocks body recycling");
        require(!PopulationRules.can_replenish(0, 60, 4000, 3599), "Nearby player blocks station replacement");
        require(PopulationRules.can_replenish(0, 45, 3600, 3600), "Distant replacement eligible after delay");
        require(!PopulationRules.can_replenish(0, float.NaN, 4000, 4000), "Invalid clock blocks recycling");
        require(!PopulationRules.can_replenish(0, 60, float.NaN, 4000), "Invalid distance blocks recycling");
        ResponseRules armed = ResponseRules.for_severity(ResponseSeverity.Armed, 4);
        ResponseRules tactical = ResponseRules.for_severity(ResponseSeverity.Tactical, 4);
        require(armed.burst_limit == 8 && armed.break_seconds == 5, "Four-player armed waves");
        require(tactical.burst_limit == 10 && tactical.break_seconds == 4, "Four-player tactical waves");
    }

    private static void check_response()
    {
        var response = new ResponseState();
        require(response.request_dispatch(100, 0, 1, DispatchSource.Station) == 0, "Inactive state cannot dispatch");
        response.begin("host", 100);
        require(response.phase == ResponsePhase.Responding, "New incident awaits contact");
        require(response.request_dispatch(105, 1, 1, DispatchSource.Nearby) == 0, "Nearby backup needs a sighting");
        response.observe(visible: true, searching: false, new Vector3(10, 0, 20), now_seconds: 100);
        require(response.request_dispatch(104, 1, 1, DispatchSource.Nearby) == 0, "Initial radio delay");
        require(response.request_dispatch(105, 4, 1, DispatchSource.Nearby) == 0, "Active ceiling");
        require(response.dispatch_count == 0, "Rejected calls preserve allowance");
        require(response.request_dispatch(105, 1, 1, DispatchSource.Nearby) == 1, "First nearby officer");
        require(response.request_dispatch(112, 2, 1, DispatchSource.Nearby) == 0, "Calls remain staggered");
        require(response.request_dispatch(113, 2, 1, DispatchSource.Nearby) == 1, "Second nearby officer");
        require(response.dispatch_seconds == 143, "Two officers trigger a 30-second break");
        require(response.request_dispatch(142.9f, 2, 1, DispatchSource.Nearby) == 0, "Cooldown boundary");
        require(response.request_dispatch(143, 2, 1, DispatchSource.Nearby) == 1, "Third nearby officer");
        require(response.request_dispatch(151, 2, 1, DispatchSource.Nearby) == 1, "Fourth nearby officer");
        require(response.request_dispatch(1000, 0, 2, DispatchSource.Station) == 0, "Station cannot bypass exhausted budget");
        require(response.rules.active_limit == 4, "Time alone does not increase severity");
        response.reset();
        require(response.phase == ResponsePhase.Inactive && response.player_code == "", "Cleanup clears identity and phase");
        require(response.dispatch_count == 0 && response.dispatch_seconds == 0, "Cleanup clears budget and cooldown");
        response.begin("host", 200);
        response.observe(true, false, Vector3.Zero, 200);
        require(response.request_dispatch(204, 1, 1, DispatchSource.Nearby) == 0, "Fresh pursuit restores delay");
        require(response.request_dispatch(205, 1, 1, DispatchSource.Nearby) == 1, "Fresh pursuit restores allowance");
    }

    private static void check_station_dispatch()
    {
        var response = new ResponseState();
        var other = new ResponseState();
        response.begin("host", 100);
        other.begin("client", 100);
        require(response.request_dispatch(100, 0, 0, DispatchSource.Station) == 0, "Empty station does not consume budget");
        require(response.request_dispatch(100, 0, 8, DispatchSource.Station) == 2, "Initial reported crime can send a bounded crew");
        require(response.request_dispatch(100, 0, 2, DispatchSource.Station) == 0, "Second station cannot dispatch concurrently");
        require(other.request_dispatch(100, 0, 2, DispatchSource.Station) == 2, "Independent player can receive a response");
        response.observe(true, false, Vector3.Zero, 101);
        require(response.request_dispatch(129, 2, 1, DispatchSource.Nearby) == 0, "Native crew pauses nearby backup");
        require(response.request_dispatch(130, 2, 8, DispatchSource.Station) == 2, "Second crew uses remaining allowance");
        require(response.request_dispatch(200, 0, 1, DispatchSource.Nearby) == 0, "Native crews exhaust nearby allowance too");
        response.reset();
        require(other.dispatch_count == 2, "One player resetting does not affect another");

        response.begin("host", 100);
        require(response.request_dispatch(100, 2, 1, DispatchSource.Station) == 1, "Reserve one incoming officer");
        response.observe(true, false, Vector3.Zero, 101);
        require(response.request_dispatch(108, 3, 1, DispatchSource.Nearby) == 0, "Incoming officer occupies a slot before arrival");
        require(response.request_dispatch(130, 3, 1, DispatchSource.Nearby) == 1, "Pending capacity expires after 30 seconds");
        require(response.dispatch_count == 2, "Expiration does not refund lifetime allowance");

        response.begin("host", 100);
        response.observe(true, false, Vector3.Zero, 100);
        require(response.request_dispatch(105, 1, 1, DispatchSource.Nearby) == 1, "Local dispatch starts shared burst");
        require(response.request_dispatch(113, 2, 8, DispatchSource.Station) == 1, "Station receives only remaining burst slot");
        require(response.request_dispatch(142, 0, 2, DispatchSource.Station) == 0, "Mixed burst uses shared cooldown");
    }

    private static void check_search()
    {
        var response = new ResponseState();
        Vector3 sighting = new(10, 2, 30);
        response.begin("host", 100);
        response.observe(true, false, sighting, 100);
        require(response.request_dispatch(105, 1, 1, DispatchSource.Nearby) == 1, "Budget spent before search");
        response.observe(false, false, new Vector3(999, 99, 999), 106);
        require(response.phase == ResponsePhase.Searching, "Losing sight enters search");
        require(response.last_known_position == sighting && response.search_started_seconds == 106, "Search freezes last sighting");
        require(response.request_dispatch(150, 1, 1, DispatchSource.Nearby) == 0, "No nearby dispatch while searching");
        require(response.request_dispatch(150, 1, 2, DispatchSource.Station) == 0, "No station dispatch while searching");
        response.observe(false, true, new Vector3(-500, 5, -500), 151);
        require(response.last_known_position == sighting && response.search_started_seconds == 106, "Hidden movement cannot move search or restart timer");
        Vector3 new_sighting = new(20, 2, 40);
        response.observe(true, true, new_sighting, 152);
        require(response.phase == ResponsePhase.Pursuing && response.last_known_position == new_sighting, "Sighting restores pursuit");
        require(response.dispatch_count == 1, "Reacquisition does not refill budget");
        require(response.request_dispatch(152, 1, 1, DispatchSource.Nearby) == 1, "Remaining budget usable after sighting");
        response.observe(false, false, sighting, 153);
        require(response.last_known_position == new_sighting, "Second search uses the newer sighting");
        response.reset();
        require(response.last_known_position == Vector3.Zero, "Reset clears search location");
        response.begin("client", 200);
        response.observe(false, true, sighting, 201);
        require(response.last_known_position == sighting, "Initial search can use reported last-known position");
        require(SearchPattern.destination(sighting, 0, 0, 0) == sighting, "First officer checks last-known position");
        Vector3 first = SearchPattern.destination(sighting, 1, 0, 0);
        Vector3 second = SearchPattern.destination(sighting, 2, 0, 0);
        require(Vector3.Distance(first, second) > 1, "Officers search different points");
        for (int index = 0; index < 128; index++)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector3 point = SearchPattern.destination(sighting, index, 300, attempt);
                require(Vector3.Distance(point, sighting) <= 12.01f, "Search remains bounded around last sighting");
                require(point.Y == sighting.Y, "Search keeps the last-known height");
            }
        }
    }

    private static void check_severity()
    {
        var response = new ResponseState();
        var other = new ResponseState();
        response.begin("host", 0);
        other.begin("client", 0);
        response.observe(true, false, Vector3.Zero, 0);
        response.observe_crime("Theft");
        require(response.severity == ResponseSeverity.Patrol, "Minor crime stays patrol level");
        require(response.rules.active_limit == 4 && response.rules.dispatch_limit == 4, "Patrol response size");
        require(response.request_dispatch(5, 1, 1, DispatchSource.Nearby) == 1, "Spend patrol allowance");
        response.observe_crime("Evading");
        require(response.severity == ResponseSeverity.Pursuit && response.rules.active_limit == 6, "Resistance expands pursuit");
        require(response.rules.dispatch_limit == 8 && response.dispatch_count == 1, "Escalation adds headroom without refunding used officers");
        response.observe_crime("DischargeFirearm");
        require(response.severity == ResponseSeverity.Armed && response.rules.active_limit == 12, "Gunfire raises armed response");
        require(response.rules.dispatch_limit == 36, "Armed response allowance");
        require(response.record_attack(1, true), "First confirmed police hit");
        require(!response.record_attack(1, true), "Duplicate network hit is ignored");
        require(response.record_attack(2, true), "Second distinct police hit");
        require(response.severity == ResponseSeverity.Armed, "Duplicates cannot trigger tactical response");
        require(response.record_attack(3, true), "Third distinct police hit");
        require(response.severity == ResponseSeverity.Tactical, "Repeated lethal attacks trigger tactical response");
        require(response.rules.active_limit == 16 && response.rules.dispatch_limit == 64, "Tactical response size");
        response.observe_level(2);
        require(response.severity == ResponseSeverity.Tactical, "Lower native level cannot erase incident severity");
        response.observe(false, false, new Vector3(999, 0, 999), 6);
        require(response.request_dispatch(100, 0, 4, DispatchSource.Station) == 0, "Even tactical responses stop during search");
        response.observe(true, false, Vector3.Zero, 101);
        require(response.dispatch_count == 1, "Reacquisition does not refund allowance");
        response.reset();
        require(response.severity == ResponseSeverity.Patrol, "Escape resets severity");
        require(other.severity == ResponseSeverity.Patrol, "Co-op severity is per player");
        response.begin("host", 200);
        response.observe_level(4);
        require(response.severity == ResponseSeverity.Armed, "Synced native lethal level supplies armed fallback");
        response.record_kill();
        require(response.severity == ResponseSeverity.Tactical, "Attributed police death triggers tactical response");

        response.begin("host", 300);
        require(response.request_dispatch(300, 0, 2, DispatchSource.Station) == 2, "Patrol burst");
        response.record_kill();
        response.observe(true, false, Vector3.Zero, 301);
        require(response.request_dispatch(301, 0, 6, DispatchSource.Station) == 0, "Emergency call waits one second");
        require(response.request_dispatch(302, 0, 6, DispatchSource.Station) == 6, "Tactical escalation interrupts old break");
        require(response.dispatch_seconds == 308, "Tactical break is six seconds");
        response.set_player_count(2);
        require(response.rules.active_limit == 24 && response.rules.dispatch_limit == 96, "Two-player tactical scaling");
        response.observe(false, false, new Vector3(999, 0, 999), 303);
        require(response.request_dispatch(308, 0, 7, DispatchSource.Station) == 7, "One radioed wave after losing sight");
        require(response.request_dispatch(309, 0, 7, DispatchSource.Station) == 0, "No repeat search wave");
        require(response.last_known_position == Vector3.Zero, "Radio wave keeps last known location");
        response.observe(true, false, Vector3.Zero, 340);
        for (int tick = 0; tick < 40; tick++) response.request_dispatch(340 + tick * 31, 0, 7, DispatchSource.Station);
        require(response.dispatch_count == 96, "Scaled allowance remains finite");
        for (int attack = 100; attack < 180; attack++) response.record_attack(attack, true);
        require(response.dispatch_count == 96 && response.severity == ResponseSeverity.Tactical, "Further violence cannot refill the tactical budget");
        require(!response.record_attack(179, true), "Recent duplicates stay suppressed after the attack buffer wraps");
        response.begin("host", 470);
        response.set_player_count(2);
        response.observe_level(4);
        response.observe(true, false, Vector3.Zero, 470);
        require(response.rules.active_limit == 18 && response.rules.dispatch_limit == 54, "Two-player armed scaling");
        response.observe(false, false, Vector3.One, 471);
        require(response.request_dispatch(472, 0, 1, DispatchSource.Nearby) == 0, "Radio exception never recruits nearby officers");
        require(response.request_dispatch(480, 0, 5, DispatchSource.Station) == 0, "Radio window expires after eight seconds");
        response.set_player_count(99);
        require(response.rules.active_limit == 30, "Scaling capped at four players");
        response.set_player_count(0);
        require(response.rules.active_limit == 12, "Scaling never falls below solo");
        response.begin("host", 500);
        require(response.record_attack(179, false), "Fresh incident clears old attack IDs");
        require(response.severity == ResponseSeverity.Pursuit, "Nonlethal police assault does not trigger armed response");
    }

    private static void check_roles()
    {
        require(ResponseRules.role(0) == OfficerRole.Chaser, "Chaser role");
        require(ResponseRules.role(1) == OfficerRole.Interceptor, "First interceptor role");
        require(ResponseRules.role(2) == OfficerRole.Support, "Support role");
        require(ResponseRules.role(3) == OfficerRole.Interceptor, "Second interceptor role");
        int armed_shotguns = 0;
        int tactical_shotguns = 0;
        int batons = 0;
        int tasers = 0;
        for (int i = 0; i < 12; i++)
        {
            require(ResponseRules.weapon(ResponseSeverity.Tactical, 2, i) == OfficerWeapon.Native, "Arrest-only pursuit retains native weapons");
            OfficerWeapon nonlethal = ResponseRules.weapon(ResponseSeverity.Pursuit, 3, i);
            if (nonlethal == OfficerWeapon.Baton) batons++;
            if (nonlethal == OfficerWeapon.Taser) tasers++;
            OfficerWeapon armed = ResponseRules.weapon(ResponseSeverity.Armed, 4, i);
            OfficerWeapon tactical = ResponseRules.weapon(ResponseSeverity.Tactical, 4, i);
            require(armed is OfficerWeapon.Pistol or OfficerWeapon.Shotgun, "Armed response has no taser-only squads");
            if (armed == OfficerWeapon.Shotgun) armed_shotguns++;
            if (tactical == OfficerWeapon.Shotgun) tactical_shotguns++;
            if (ResponseRules.role(i) == OfficerRole.Support)
                require(tactical == OfficerWeapon.Pistol, "Support officer keeps a pistol");
        }
        require(batons == 4 && tasers == 8, "Nonlethal response mixes batons and tasers");
        require(armed_shotguns == 3 && tactical_shotguns == 6, "Tactical squads contain a larger shotgun share");
        require(ResponseRules.firing_distance(OfficerRole.Support, OfficerWeapon.Pistol) >
            ResponseRules.firing_distance(OfficerRole.Chaser, OfficerWeapon.Pistol), "Support keeps more distance");
        require(ResponseRules.firing_distance(OfficerRole.Interceptor, OfficerWeapon.Shotgun) <
            ResponseRules.firing_distance(OfficerRole.Support, OfficerWeapon.Pistol), "Shotguns work closer than pistol support");
        require(ResponseRules.firing_distance(OfficerRole.Support, OfficerWeapon.Taser) == 0, "Taser spacing stays native");
    }

    private static void check_interception()
    {
        require(!Interception.try_offset(Vector2.Zero, new Vector2(6, -6), 0, out _), "Stationary players retain normal pursuit");
        require(!Interception.try_offset(new Vector2(0.5f, 0), new Vector2(6, -6), 0, out _), "Ignore motion jitter");
        require(!Interception.try_offset(new Vector2(13, 0), new Vector2(6, -6), 0, out _), "Reject implausible motion");
        require(!Interception.try_offset(new Vector2(float.NaN, 0), new Vector2(6, -6), 0, out _), "Reject invalid motion");
        require(!Interception.try_offset(new Vector2(float.PositiveInfinity, 0), new Vector2(6, -6), 0, out _), "Reject infinite motion");
        require(Interception.try_offset(new Vector2(4, 0), new Vector2(6, -6), 0, out Vector2 right), "Moving target can be intercepted");
        require(right == new Vector2(5, -6), "Lead follows eastward movement");
        require(Interception.try_offset(new Vector2(4, 0), new Vector2(6, -6), 1, out Vector2 left), "Second interceptor");
        require(left == new Vector2(-2, 6), "Interceptors use opposite sides");
        require(Interception.try_offset(new Vector2(-4, 0), new Vector2(-6, 6), 0, out Vector2 reverse), "Reverse movement");
        require(reverse == -right, "Prediction reverses with movement");
        require(Interception.try_offset(new Vector2(0, 12), new Vector2(6, -6), 0, out Vector2 fast), "Bounded fast prediction");
        require(fast == new Vector2(6, 8), "Lead distance is capped at eight metres");
        require(Interception.try_offset(new Vector2(0, 1), new Vector2(6, -6), 0, out Vector2 slow), "Walking prediction");
        require(slow == new Vector2(6, 2), "Short lead at walking speed");
        require(Interception.try_offset(new Vector2(4, 0), new Vector2(-15, 0), 0, out Vector2 approach), "Flank approach available");
        require(approach == new Vector2(-8, -6), "Spread sideways before advancing");
        require(Interception.try_offset(new Vector2(4, 0), approach, 0, out Vector2 cutoff), "Advance from flank");
        require(cutoff == new Vector2(5, -6), "Cut ahead after lateral separation");
        require(!Interception.try_offset(new Vector2(4, 0), new Vector2(float.NaN, 0), 0, out _), "Invalid officer position rejected");
        for (int first = 0; first < 24; first++)
        {
            require(Interception.try_offset(new Vector2(4, 0), new Vector2(-15, 0), first, out Vector2 first_point), "Flank slot available");
            for (int second = first + 1; second < 24; second++)
            {
                require(Interception.try_offset(new Vector2(4, 0), new Vector2(-15, 0), second, out Vector2 second_point), "Other flank slot available");
                require(Vector2.Distance(first_point, second_point) >= 1.99f, "Large groups get distinct flank destinations");
            }
        }
        require(!Interception.try_offset(new Vector2(4, 0), Vector2.Zero, 64, out _), "Flank slots bounded");
        Vector2 push = PursuitSpacing.separation(new Vector2(0, 1), 1, 2);
        require(push == new Vector2(0, 2), "Nearby officers push apart");
        require(PursuitSpacing.separation(new Vector2(0, 4), 1, 2) == Vector2.Zero, "Distant officers do not affect spacing");
        require(PursuitSpacing.separation(Vector2.Zero, 1, 2) == -PursuitSpacing.separation(Vector2.Zero, 2, 1), "Overlapping officers separate in opposite directions");
        require(PursuitSpacing.lateral_offset(new Vector2(10, 10), new Vector2(10, 0)) == new Vector2(0, 2.5f), "Spacing stays lateral and bounded");
        require(PursuitSpacing.lateral_offset(push, new Vector2(1, 0)) == Vector2.Zero, "Melee contact retains native movement");
        require(PursuitSpacing.lateral_offset(new Vector2(float.NaN, 0), Vector2.One) == Vector2.Zero, "Invalid spacing ignored");
    }

    private static void check_stuck_recovery()
    {
        var recovery = new StuckRecovery();
        require(!recovery.try_repath(100, Vector3.Zero, true), "First observation does not trigger recovery");
        require(!recovery.try_repath(105.9f, Vector3.Zero, true), "Stuck detection waits six seconds");
        require(recovery.try_repath(106, Vector3.Zero, true), "Stationary officer requests a fresh path");
        require(!recovery.try_repath(107, Vector3.Zero, true), "No repeated path request every tick");
        require(recovery.try_repath(112, Vector3.Zero, true), "Second bounded retry");
        require(recovery.try_repath(118, Vector3.Zero, true), "Third bounded retry");
        require(!recovery.try_repath(200, Vector3.Zero, true), "Unreachable position cannot retry forever");
        require(!recovery.try_repath(201, new Vector3(1, 0, 0), true), "Real movement resets retry budget");
        require(recovery.try_repath(207, new Vector3(1, 0, 0), true), "Later obstruction can recover");
        require(!recovery.try_repath(208, new Vector3(1, 0, 0), false), "Paused movement resets detection");
        require(!recovery.try_repath(300, Vector3.Zero, true), "Resume begins a new observation window");
        require(!recovery.try_repath(305, new Vector3(0.6f, 0, 0), true), "Slow progress prevents false detection");
        require(!recovery.try_repath(310, new Vector3(0.6f, 0, 0), true), "Progress restarts timer");
        require(recovery.try_repath(311, new Vector3(0.6f, 0, 0), true), "Recovery after progress stops");
        recovery.reset();
        require(!recovery.try_repath(400, Vector3.Zero, true), "New officer starts clean");
        require(!recovery.try_repath(406, new Vector3(float.NaN, 0, 0), true), "Invalid positions cannot trigger recovery");
        require(!recovery.try_repath(410, Vector3.Zero, true), "Invalid sample clears history");
        require(!recovery.try_repath(1, Vector3.Zero, true), "Clock reset clears history");
    }

    private static void require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
