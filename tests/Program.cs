using System;
using System.Numerics;
using CoordinatedPolice;

internal static class Program
{
    private static void Main()
    {
        uint first_day = DailyChance.score("test-player", 1);
        if (first_day != DailyChance.score("test-player", 1) || first_day == DailyChance.score("test-player", 2))
            throw new InvalidOperationException("Daily cosmetic selection must survive reload and vary by day.");
        int rare_days = 0;
        for (int day = 0; day < 10000; day++)
            if (DailyChance.score("test-player", day) % 100 == 0) rare_days++;
        if (rare_days < 50 || rare_days > 150) throw new InvalidOperationException("Daily cosmetic probability drift.");
        if (DailyChance.score("test-player", -1) != uint.MaxValue) throw new InvalidOperationException("Invalid day rejected.");

        check_configuration();
        check_districts();
        check_population();
        check_response();
        check_station_dispatch();
        check_search();
        check_severity();
        check_roles();
        check_interception();
        check_stuck_recovery();
        Console.WriteLine("Configuration, district allocation, population, severity, dispatch, roles, search, interception, and recovery checks passed.");
    }

    private static void check_configuration()
    {
        PoliceSettings defaults = PoliceSettings.current;
        try
        {
            PoliceSettings invalid = new PoliceSettings
            {
                patrols_per_player = 100,
                reserve_base = -1,
                reserves_per_extra_player = 99,
                response_size = float.NaN,
                reinforcement_time = float.PositiveInfinity,
                northtown_weight = -10,
                uptown_weight = 100
            }.validated();
            require(invalid.patrols_per_player == 16 && invalid.reserve_base == 0 &&
                invalid.reserves_per_extra_player == 16, "Population settings are bounded");
            require(invalid.response_size == 1 && invalid.reinforcement_time == 1, "Non-finite settings use defaults");
            require(invalid.northtown_weight == 0 && invalid.uptown_weight == 10, "District weights are bounded");
            PoliceSettings.current = new PoliceSettings
            {
                patrols_per_player = 8,
                reserve_base = 12,
                reserves_per_extra_player = 5,
                response_size = 1.5f,
                reinforcement_time = 0.5f
            }.validated();
            require(PopulationRules.for_players(4) == new PopulationRules(32, 27), "Host population settings scale to four players");
            ResponseRules response = ResponseRules.for_severity(ResponseSeverity.Armed, 4);
            require(response.active_limit == 45 && response.dispatch_limit == 135 && response.burst_limit == 12,
                "Response multiplier applies after player scaling");
            require(response.interval_seconds == 1 && response.break_seconds == 2.5f, "Timing multiplier controls spacing and breaks");
            var state = new ResponseState();
            state.begin("host", 0);
            require(state.dispatch_seconds == 2.5f, "Initial nearby response uses configured timing");
            state.observe_crime("DischargeFirearm");
            state.observe(true, false, Vector3.Zero, 0);
            require(state.dispatch_seconds == 0.5f, "Urgent reinforcement delay uses configured timing");
            require(state.request_dispatch(0.5f, 0, 6, DispatchSource.Nearby) == 6, "Configured burst admitted");
            require(state.dispatch_seconds == 4.5f, "Configured solo armed burst break applied");
            PoliceSettings.current = new PoliceSettings
            {
                patrols_per_player = 99,
                reserve_base = 999,
                reserves_per_extra_player = 999,
                response_size = 999,
                reinforcement_time = -1
            }.validated();
            require(PopulationRules.for_players(99) == new PopulationRules(64, 112), "Extreme population configuration is bounded");
            ResponseRules capped = ResponseRules.for_severity(ResponseSeverity.Tactical, 99);
            require(capped.active_limit == 64 && capped.burst_limit == 16 && capped.dispatch_limit == 320,
                "Extreme response settings retain bounded active and burst limits");
            require(capped.interval_seconds == 0.25f && capped.break_seconds == 1f, "Positive minimum timing preserved");
        }
        finally { PoliceSettings.current = defaults; }
    }

    private static void check_districts()
    {
        bool[] eligible = { true, true, true, true, true, true };
        int[] weights = { 1, 1, 1, 1, 1, 1 };
        int[] targets = new int[6];
        DistrictAllocation.allocate(20, eligible, weights, targets);
        require(targets[0] == 4 && targets[1] == 4 && targets[2] == 3 && targets[5] == 3,
            "Twenty patrols distribute deterministically across six districts");
        eligible = new[] { true, true, false, false, false, false };
        DistrictAllocation.allocate(20, eligible, weights, targets);
        require(targets[0] == 10 && targets[1] == 10 && targets[2] == 0, "Locked or unrouted districts receive no allocation");
        weights[0] = 2;
        DistrictAllocation.allocate(20, eligible, weights, targets);
        require(targets[0] == 13 && targets[1] == 7, "Weights divide the global total rather than multiplying it");
        weights[0] = 0;
        DistrictAllocation.allocate(20, eligible, weights, targets);
        require(targets[0] == 0 && targets[1] == 20, "Zero district weight excludes supplemental patrol assignment");
        Array.Clear(weights);
        DistrictAllocation.allocate(20, eligible, weights, targets);
        require(Array.TrueForAll(targets, value => value == 0), "All-zero weights do not divide by zero");
        for (int mask = 0; mask < 64; mask++)
        {
            for (int i = 0; i < 6; i++) { eligible[i] = (mask & (1 << i)) != 0; weights[i] = i + 1; }
            for (int total = 0; total <= 64; total++)
            {
                DistrictAllocation.allocate(total, eligible, weights, targets);
                int sum = 0;
                for (int i = 0; i < 6; i++)
                {
                    sum += targets[i];
                    require(targets[i] >= 0 && (eligible[i] || targets[i] == 0), "Allocation never escapes eligible districts");
                }
                require(sum == (mask == 0 ? 0 : total), "All eligibility combinations preserve the patrol budget");
            }
        }
        int[] districts = { 0, 0, 1, 2 };
        int[] members = { 3, 0, 0, 0 };
        bool[] available = { true, true, true, true };
        int[] actual = { 1, 0, 0, 0, 0, 0 };
        targets = new[] { 5, 2, 0, 0, 0, 0 };
        require(DistrictAllocation.choose_route(districts, members, available, actual, targets, 0) == 1,
            "Largest deficit wins with least-used route inside the district");
        available[1] = false;
        require(DistrictAllocation.choose_route(districts, members, available, actual, targets, 0) == 0,
            "Unavailable route does not block another route");
        available[0] = false;
        require(DistrictAllocation.choose_route(districts, members, available, actual, targets, 0) == 2,
            "Unavailable district routes do not block another underserved district");
        actual[1] = 2;
        require(DistrictAllocation.choose_route(districts, members, available, actual, targets, 0) == -1,
            "No surplus or zero-target deployment when eligible deficits are filled");
        require(DistrictAllocation.choose_route(Array.Empty<int>(), Array.Empty<int>(), Array.Empty<bool>(), actual, targets, 0) == -1,
            "Empty route list is safe");
        districts = new[] { 0, 1 };
        members = new[] { 0, 0 };
        available = new[] { true, true };
        actual = new int[6];
        targets = new[] { 2, 2, 0, 0, 0, 0 };
        require(DistrictAllocation.choose_route(districts, members, available, actual, targets, 1) == 1,
            "Equal deficits rotate fairly through the route cursor");
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
        for (int step = 0; step <= 10; step++)
        {
            for (int i = 0; i < 128; i++)
            {
                Vector3 point = SearchPattern.destination(sighting, i, step * 6, 0);
                for (int j = 0; j < i; j++)
                    require(Vector3.Distance(point, SearchPattern.destination(sighting, j, step * 6, 0)) > 1.5f,
                        "All 128 search slots stay distinct throughout expansion");
            }
        }
        Vector3 first = SearchPattern.destination(sighting, 1, 0, 0);
        Vector3 second = SearchPattern.destination(sighting, 2, 0, 0);
        require(Vector3.Distance(first, second) > 1, "Officers search different points");
        for (int index = 0; index < 128; index++)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector3 point = SearchPattern.destination(sighting, index, 300, attempt);
                require(Vector3.Distance(point, sighting) <= 33.01f, "Search remains bounded around last sighting");
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
        Vector2[] positions = { new(20, -8), new(18, 8), new(-25, 0), new(2, 0), new(-4, 0) };
        Vector2 velocity = new(4, 0);
        require(Interception.assign(positions, 3, velocity).role == OfficerRole.Chaser,
            "Closest officer chases regardless of registry slot");
        require(Interception.assign(positions, 4, velocity).role == OfficerRole.Chaser,
            "Larger pursuit keeps two close chasers");
        PursuitAssignment right = Interception.assign(positions, 0, velocity);
        PursuitAssignment left = Interception.assign(positions, 1, velocity);
        require(right.role == OfficerRole.Interceptor && left.role == OfficerRole.Interceptor,
            "Officers ahead take interception roles");
        require(right.redirect && left.redirect && right.offset.Y < 0 && left.offset.Y > 0,
            "Interceptors stay on their existing side");
        require(right.offset.X == 6f && left.offset.X == 6f, "Cutoff uses bounded motion prediction");
        PursuitAssignment trailing = Interception.assign(positions, 2, velocity);
        require(trailing.role == OfficerRole.Support && trailing.offset.X < 0,
            "Trailing backup approaches from behind instead of crossing through the player");
        foreach (Vector2 invalid_velocity in new[] { Vector2.Zero, new Vector2(0.1f, 0), new Vector2(13, 0),
            new Vector2(float.NaN, 0), new Vector2(float.PositiveInfinity, 0) })
            require(Interception.assign(positions, 0, invalid_velocity).role == OfficerRole.Support,
                "Stationary or invalid motion disables prediction while preserving approaches");
        positions[0] = new Vector2(-2, -18);
        require(Interception.assign(positions, 0, velocity).role == OfficerRole.Interceptor,
            "Officer beside the route can intercept");
        positions[0] = new Vector2(-20, -18);
        require(Interception.assign(positions, 0, velocity).role == OfficerRole.Support,
            "Officer well behind is not sent on a long cutoff");
        positions[0] = new Vector2(1, 0);
        require(Interception.assign(positions, 0, velocity).role == OfficerRole.Chaser &&
            Interception.assign(positions, 4, velocity).role == OfficerRole.Support,
            "Roles change when a different officer becomes closer");
        positions[0] = new Vector2(float.NaN, float.NaN);
        require(!Interception.assign(positions, 0, velocity).redirect, "Missing officer cannot receive a destination");
        require(Interception.assign(positions, 4, velocity).role == OfficerRole.Support,
            "A four-officer group reserves only the nearest chaser");
        Vector2[] mirrored = { new(20, -8), new(2, 0) };
        PursuitAssignment forward = Interception.assign(mirrored, 0, velocity);
        mirrored[0] = -mirrored[0];
        mirrored[1] = -mirrored[1];
        require(Interception.assign(mirrored, 0, -velocity).offset == -forward.offset,
            "Reversing the complete scene reverses cutoff geometry");
        Vector2[] backup = new Vector2[128];
        Array.Fill(backup, new Vector2(-40, 0));
        for (int i = 0; i < backup.Length; i++)
        {
            PursuitAssignment assignment = Interception.assign(backup, i, velocity);
            require(float.IsFinite(assignment.offset.LengthSquared()), "Large groups produce finite plans");
            if (i < 2) require(assignment.role == OfficerRole.Chaser, "Equal distances use deterministic chasers");
            if (!assignment.redirect) continue;
            require(assignment.offset.Length() <= 21.01f, "Incoming approaches remain local");
            for (int j = 0; j < i; j++)
            {
                PursuitAssignment previous = Interception.assign(backup, j, velocity);
                if (previous.redirect) require(Vector2.Distance(assignment.offset, previous.offset) > 1.5f,
                    "Backup from one direction receives distinct approaches");
            }
        }
        Vector2[] other_incident = { new(1, 0) };
        require(Interception.assign(other_incident, 0, velocity).role == OfficerRole.Chaser,
            "Another target has its own chaser even during a large response");
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
