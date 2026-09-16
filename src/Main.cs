using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Combat;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using MelonLoader;
using UnityEngine;
using UnityEngine.AI;

[assembly: MelonInfo(typeof(CoordinatedPolice.Main), "Coordinated Police", "0.8.0", "holyfurries")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace CoordinatedPolice;

public sealed class Main : MelonMod
{
    private const int player_limit = 16;
    private const int officer_limit = 128;
    private static readonly ResponseState[] responses = new ResponseState[player_limit];
    private static readonly PoliceOfficer?[] officers = new PoliceOfficer?[officer_limit];
    private static readonly float[] redirects_resume_seconds = new float[officer_limit];
    private static readonly float[] intercept_seconds = new float[officer_limit];
    private static readonly Vector3[] intercept_origins = new Vector3[officer_limit];
    private static readonly Vector3[] intercept_positions = new Vector3[officer_limit];
    private static readonly OfficerRole[] pursuit_roles = new OfficerRole[officer_limit];
    private static readonly bool[] intercept_valid = new bool[officer_limit];
    private static readonly string?[] intercept_targets = new string?[officer_limit];
    private static readonly StuckRecovery[] recoveries = new StuckRecovery[officer_limit];
    private static readonly SearchCache[] searches = new SearchCache[officer_limit];
    private struct SearchCache
    {
        public string? player_code;
        public int slot;
        public Vector3 origin;
        public Vector3 destination;
        public float expires_seconds;
        public bool valid;
    }
    private static readonly ResponseLog[] response_logs = new ResponseLog[player_limit];
    private struct ResponseLog
    {
        public string? player_code;
        public ResponsePhase phase;
        public ResponseSeverity severity;
        public int active_limit;
        public float next_seconds;
    }
    private bool? logged_server;
    private int logged_players = -1;
    private static int officer_count;
    private static bool running;
    internal static bool authoritative => running && InstanceFinder.IsServer;
    private float next_tick;
    private static int search_queries_count;
    private static float search_queries_reset_seconds;

    public override void OnInitializeMelon()
    {
        PoliceConfiguration.load();
        for (int i = 0; i < responses.Length; i++) responses[i] = new ResponseState();
        for (int i = 0; i < recoveries.Length; i++) recoveries[i] = new StuckRecovery();
        HarmonyInstance.Patch(AccessTools.Method(typeof(NPCMovement), nameof(NPCMovement.SetDestination),
            new[] { typeof(Vector3) }), prefix: new HarmonyMethod(typeof(Main), nameof(redirect_destination)));
        HarmonyInstance.Patch(AccessTools.Method(typeof(PoliceStation), nameof(PoliceStation.Dispatch)),
            prefix: new HarmonyMethod(typeof(Main), nameof(coordinate_dispatch)));
        HarmonyInstance.Patch(AccessTools.Method(typeof(CombatBehaviour), nameof(CombatBehaviour.GetNextSearchLocation)),
            prefix: new HarmonyMethod(typeof(Main), nameof(redirect_search)));
        PoliceTactics.install(HarmonyInstance);
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (sceneName != "Main") return;
        running = true;
        foreach (MelonMod mod in RegisteredMelons)
        {
            if (!mod.Info.Name.Contains("PoliceResponseOverhaul", StringComparison.OrdinalIgnoreCase) &&
                !mod.Info.Name.Contains("Police Response Overhaul", StringComparison.OrdinalIgnoreCase) &&
                !mod.Info.Name.Contains("HardcorePolice", StringComparison.OrdinalIgnoreCase)) continue;
            running = false;
            LoggerInstance.Error("Police: Disable other police overhauls before testing Coordinated Police.");
        }
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        if (sceneName != "Main") return;
        running = false;
        officer_count = 0;
        logged_server = null;
        logged_players = -1;
        Array.Clear(response_logs, 0, response_logs.Length);
        PoliceTactics.reset();
        PolicePopulation.reset();
        PoliceNavigation.reset();
        Array.Clear(redirects_resume_seconds, 0, redirects_resume_seconds.Length);
        Array.Clear(officers, 0, officers.Length);
        Array.Clear(intercept_seconds, 0, intercept_seconds.Length);
        Array.Clear(intercept_targets, 0, intercept_targets.Length);
        Array.Clear(searches, 0, searches.Length);
        foreach (ResponseState response in responses) response.reset();
        foreach (StuckRecovery recovery in recoveries) recovery.reset();
        next_tick = 0;
        search_queries_count = 0;
        search_queries_reset_seconds = 0;
    }

    public override void OnUpdate()
    {
        if (!running || Time.time < next_tick) return;
        next_tick = Time.time + 1f;
        try
        {
            bool server = InstanceFinder.IsServer;
            int connected_count = Player.PlayerList.Count;
            if (logged_server != server || logged_players != connected_count)
            {
                LoggerInstance.Msg($"Police: Network authority={(server ? "host" : "client/waiting")}, players={connected_count}, scaling_players={Math.Clamp(connected_count, 1, 4)}. {(server ? "Response decisions run here." : "Response decisions require the host; this peer does not dispatch police.")}");
                logged_server = server;
                logged_players = connected_count;
            }
            PolicePopulation.tick();
            if (!server) return;
            officer_count = Math.Min(PoliceOfficer.Officers.Count, officer_limit);
            for (int i = 0; i < officer_count; i++)
            {
                PoliceOfficer officer = PoliceOfficer.Officers[i];
                if (officers[i] != officer)
                {
                    redirects_resume_seconds[i] = 0;
                    intercept_seconds[i] = 0;
                    intercept_valid[i] = false;
                    recoveries[i].reset();
                    searches[i] = default;
                }
                officers[i] = officer;
            }
            recover_stuck_officers();
            int count = Math.Min(Player.PlayerList.Count, player_limit);
            for (int i = 0; i < responses.Length; i++)
            {
                bool present = false;
                for (int j = 0; j < count; j++)
                {
                    Player player = Player.PlayerList[j];
                    if (player != null && player.PlayerCode == responses[i].player_code) present = true;
                }
                if (!present) responses[i].reset();
            }
            for (int i = 0; i < count; i++)
            {
                Player player = Player.PlayerList[i];
                if (player == null) continue;
                bool wanted = player.CrimeData != null && player.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None;
                ResponseState? response = get_response(player.PlayerCode, create: wanted);
                if (response != null) update_response(player, response);
            }
            log_responses();
        }
        catch (Exception error)
        {
            running = false;
            LoggerInstance.Error($"Police: Disabled after a game API failure: {error}");
        }
    }

    private static void log_responses()
    {
        for (int i = 0; i < responses.Length; i++)
        {
            ResponseState response = responses[i];
            ref ResponseLog previous = ref response_logs[i];
            if (previous.player_code != response.player_code && !string.IsNullOrEmpty(previous.player_code))
                MelonLogger.Msg($"Police: Response ended player={previous.player_code}.");
            if (response.phase == ResponsePhase.Inactive)
            {
                previous = default;
                continue;
            }
            bool changed = previous.player_code != response.player_code || previous.phase != response.phase ||
                previous.severity != response.severity || previous.active_limit != response.rules.active_limit;
            if (!changed && Time.time < previous.next_seconds) continue;
            var known = response.last_known_position;
            PoliceStation station = PoliceStation.GetClosestPoliceStation(new Vector3(known.X, known.Y, known.Z));
            int pool_count = station == null ? 0 : station.OfficerPool.Count;
            int active_count = 0;
            bool visible = false;
            int count = Math.Min(Player.PlayerList.Count, player_limit);
            for (int j = 0; j < count; j++)
            {
                Player player = Player.PlayerList[j];
                if (player == null || player.PlayerCode != response.player_code) continue;
                var snapshot = read_pursuit(player);
                active_count = snapshot.count;
                visible = snapshot.visible;
                break;
            }
            MelonLogger.Msg($"Police: Response player={response.player_code}, tier={response.severity}, phase={response.phase}, visible={visible}, active={active_count}/{response.rules.active_limit}, allowance_used={response.dispatch_count}/{response.rules.dispatch_limit}, next_call_seconds={Math.Max(0f, response.dispatch_seconds - Time.time):F1}, station_pool={pool_count}, search_wave_eligible={response.can_send_search_wave(Time.time)}.");
            previous = new ResponseLog
            {
                player_code = response.player_code,
                phase = response.phase,
                severity = response.severity,
                active_limit = response.rules.active_limit,
                next_seconds = Time.time + 10f
            };
        }
    }

    private static void recover_stuck_officers()
    {
        int attempts = 0;
        for (int i = 0; i < officer_count; i++)
        {
            PoliceOfficer? officer = officers[i];
            if (!available(officer) ||
                (!(officer!.PursuitBehaviour.Active && officer.PursuitBehaviour.TargetPlayer != null) &&
                 !(officer.FootPatrolBehaviour != null && officer.FootPatrolBehaviour.Active)))
            {
                recoveries[i].reset();
                continue;
            }
            NPCMovement movement = officer.Movement;
            NavMeshAgent agent = movement.Agent;
            Vector3 position = movement.FootPosition;
            Vector3 destination = movement.CurrentDestination;
            if (agent != null && agent.enabled && agent.isOnNavMesh && agent.pathPending) continue;
            bool eligible = movement.HasDestination && !movement.IsPaused && !movement.IsOnLadder &&
                !movement.Disoriented && movement.CanMove() && agent != null && agent.enabled && agent.isOnNavMesh &&
                !agent.isStopped && !agent.isOnOffMeshLink &&
                (destination - position).sqrMagnitude > 4f;
            if (attempts >= 2 && eligible) continue;
            if (!recoveries[i].try_repath(Time.time, new System.Numerics.Vector3(position.x, position.y, position.z), eligible)) continue;
            attempts++;
            redirects_resume_seconds[i] = Time.time + 12f;
            intercept_seconds[i] = 0;
            intercept_valid[i] = false;
            searches[i] = default;
            var pursuit = officer.PursuitBehaviour;
            if (pursuit.Active && pursuit.TargetPlayer != null)
            {
                if (pursuit.IsTargetImmediatelyVisible)
                    destination = pursuit.TargetPlayer.transform.position;
                else
                {
                    ResponseState? response = get_response(pursuit.TargetPlayer.PlayerCode, create: false);
                    if (response != null && response.phase != ResponsePhase.Inactive)
                    {
                        var known = response.last_known_position;
                        destination = new Vector3(known.X, known.Y, known.Z);
                    }
                }
            }
            if (PoliceNavigation.try_destination(movement, destination, out Vector3 recovered))
            {
                movement.SetDestination(recovered);
                MelonLogger.Msg($"Police: Navigation recovery requested officer={officer.ID}, duty={(pursuit.Active ? "pursuit" : "patrol")}, custom_offsets_paused_seconds=12.");
            }
            else
            {
                MelonLogger.Warning($"Police: Navigation recovery has no complete safe path officer={officer.ID}, path_status={agent!.pathStatus}; custom offsets paused for 12 seconds.");
            }
        }
    }

    private static void update_response(Player player, ResponseState response)
    {
        if (player.CrimeData == null || !player.IsCurrentlyTargetable || player.IsArrested ||
            player.Health == null || !player.Health.IsAlive)
        {
            response.reset();
            return;
        }
        if (player.CrimeData.CurrentPursuitLevel == PlayerCrimeData.EPursuitLevel.None)
        {
            if (response.phase == ResponsePhase.Responding && Time.time - response.started_seconds < 30f) return;
            response.reset();
            return;
        }
        var snapshot = read_pursuit(player);
        observe_response(player, response, snapshot);
        if ((!snapshot.visible && !response.can_send_search_wave(Time.time)) || Time.time < response.dispatch_seconds || response.dispatch_count >= response.rules.dispatch_limit) return;
        if (!snapshot.visible)
        {
            var known = response.last_known_position;
            PoliceStation search_station = PoliceStation.GetClosestPoliceStation(new Vector3(known.X, known.Y, known.Z));
            if (search_station != null && search_station.OfficerPool.Count > 0)
                search_station.Dispatch(response.rules.burst_limit, player, PoliceStation.EDispatchType.OnFoot, false);
            return;
        }
        PoliceOfficer? nearest = null;
        float distance_squared_min = 60f * 60f;
        for (int i = 0; i < officer_count; i++)
        {
            PoliceOfficer? officer = officers[i];
            if (!available(officer) || officer!.PursuitBehaviour.Active ||
                (officer.VehiclePursuitBehaviour != null && officer.VehiclePursuitBehaviour.Active) ||
                officer.PursuitTarget != null) continue;
            float distance_squared = (officer.transform.position - snapshot.witness).sqrMagnitude;
            if (distance_squared >= distance_squared_min) continue;
            nearest = officer;
            distance_squared_min = distance_squared;
        }
        if (nearest != null)
        {
            if (response.request_dispatch(Time.time, snapshot.count, 1, DispatchSource.Nearby) > 0)
            {
                nearest.BeginFootPursuit_Networked(player.PlayerCode, true);
                MelonLogger.Msg($"Police: Nearby pursuit requested player={player.PlayerCode}, officers=1, allowance_used={response.dispatch_count}/{response.rules.dispatch_limit}.");
            }
            return;
        }
        PoliceStation station = PoliceStation.GetClosestPoliceStation(snapshot.witness);
        if (station != null && station.OfficerPool.Count > 0)
        {
            station.Dispatch(response.rules.burst_limit, player, PoliceStation.EDispatchType.Auto, true);
        }
    }

    private static (int count, bool visible, bool searching, Vector3 witness) read_pursuit(Player player)
    {
        int count = 0;
        bool visible = false;
        bool searching = false;
        Vector3 witness = default;
        int limit = Math.Min(PoliceOfficer.Officers.Count, officer_limit);
        for (int i = 0; i < limit; i++)
        {
            PoliceOfficer officer = PoliceOfficer.Officers[i];
            if (officer == null || !officer.gameObject.activeInHierarchy || !officer.IsConscious) continue;
            var pursuit = officer.PursuitBehaviour;
            var vehicle_pursuit = officer.VehiclePursuitBehaviour;
            bool on_foot = pursuit != null && pursuit.Active && pursuit.TargetPlayer != null &&
                pursuit.TargetPlayer.PlayerCode == player.PlayerCode;
            bool in_vehicle = vehicle_pursuit != null && vehicle_pursuit.Active && vehicle_pursuit.Target != null &&
                vehicle_pursuit.Target.PlayerCode == player.PlayerCode;
            if (!on_foot && !in_vehicle) continue;
            count++;
            if (on_foot && pursuit!.IsSearching) searching = true;
            if (!(on_foot && pursuit!.IsTargetImmediatelyVisible) &&
                !(in_vehicle && vehicle_pursuit!.IsTargetImmediatelyVisible)) continue;
            visible = true;
            witness = officer.transform.position;
        }
        return (count, visible, searching, witness);
    }

    private static void observe_response(Player player, ResponseState response,
        (int count, bool visible, bool searching, Vector3 witness) snapshot)
    {
        response.set_player_count(Math.Min(Player.PlayerList.Count, player_limit));
        response.observe_level((int)player.CrimeData.CurrentPursuitLevel);
        Vector3 known_position = snapshot.visible ? player.transform.position : player.CrimeData.LastKnownPosition;
        response.observe(snapshot.visible, snapshot.searching,
            new System.Numerics.Vector3(known_position.x, known_position.y, known_position.z), Time.time);
    }

    internal static ResponseState? get_response(string player_code, bool create)
    {
        if (string.IsNullOrEmpty(player_code)) return null;
        ResponseState? empty = null;
        for (int i = 0; i < responses.Length; i++)
        {
            ResponseState response = responses[i];
            if (response.player_code == player_code) return response;
            if (empty == null && response.player_code.Length == 0) empty = response;
        }
        if (!create || empty == null) return null;
        empty.begin(player_code, Time.time);
        return empty;
    }

    private static bool coordinate_dispatch(PoliceStation __instance, ref int __0, Player __1,
        ref PoliceStation.EDispatchType __2, ref bool __3)
    {
        if (!running || !InstanceFinder.IsServer || __1 == null || __0 <= 0) return true;
        try
        {
            if (__1.CrimeData == null || __1.IsArrested || __1.Health == null || !__1.Health.IsAlive) return false;
            ResponseState? response = get_response(__1.PlayerCode, create: true);
            if (response == null) return false;
            var snapshot = read_pursuit(__1);
            observe_response(__1, response, snapshot);
            if (response.phase == ResponsePhase.Searching)
            {
                __2 = PoliceStation.EDispatchType.OnFoot;
                __3 = false;
            }
            int requested_count = Math.Min(__0, __instance.OfficerPool.Count);
            __0 = response.request_dispatch(Time.time, snapshot.count, requested_count, DispatchSource.Station);
            if (__0 == 0) return false;
            if (__0 % 2 != 0) __2 = PoliceStation.EDispatchType.OnFoot;
            MelonLogger.Msg($"Police: Station dispatch approved player={__1.PlayerCode}, officers={__0}, type={__2}, sighted={__3}, phase={response.phase}, pool_before={__instance.OfficerPool.Count}, allowance_used={response.dispatch_count}/{response.rules.dispatch_limit}. Arrival is not confirmed.");
            return true;
        }
        catch (Exception error)
        {
            running = false;
            MelonLogger.Error($"Police: Disabled dispatch coordination after a game API failure: {error}");
            return false;
        }
    }

    private static bool redirect_search(CombatBehaviour __instance, ref Vector3 __result)
    {
        if (!running || !InstanceFinder.IsServer) return true;
        try
        {
            PursuitBehaviour? pursuit = __instance.TryCast<PursuitBehaviour>();
            if (pursuit == null || !pursuit.Active) return true;
            Player player = pursuit.TargetPlayer;
            if (player == null || player.CrimeData == null ||
                player.CrimeData.CurrentPursuitLevel == PlayerCrimeData.EPursuitLevel.None) return true;
            ResponseState? response = get_response(player.PlayerCode, create: true);
            if (response == null) return true;
            var snapshot = read_pursuit(player);
            observe_response(player, response, snapshot);
            if (response.phase != ResponsePhase.Searching) return true;
            int rank = 0;
            int index = -1;
            for (int i = 0; i < officer_count; i++)
            {
                PoliceOfficer? officer = officers[i];
                if (!available(officer)) continue;
                var other = officer!.PursuitBehaviour;
                if (!other.Active || other.TargetPlayer == null || other.TargetPlayer.PlayerCode != player.PlayerCode) continue;
                if (other.Pointer == pursuit.Pointer)
                {
                    index = i;
                    break;
                }
                rank++;
            }
            if (index < 0) return true;
            var known = response.last_known_position;
            Vector3 origin = new(known.X, known.Y, known.Z);
            ref SearchCache cache = ref searches[index];
            if (cache.player_code != player.PlayerCode || cache.slot != rank || (cache.origin - origin).sqrMagnitude > 0.01f ||
                Time.time >= cache.expires_seconds)
            {
                if (Time.time >= search_queries_reset_seconds)
                {
                    search_queries_count = 0;
                    search_queries_reset_seconds = Time.time + 1f;
                }
                if (search_queries_count >= 8) return true;
                cache = new SearchCache { player_code = player.PlayerCode, slot = rank, origin = origin, expires_seconds = Time.time + 6f };
                for (int attempt = 0; attempt < 4 && search_queries_count < 8; attempt++)
                {
                    search_queries_count++;
                    var point = SearchPattern.destination(known, rank, Time.time - response.search_started_seconds, attempt);
                    Vector3 candidate = new(point.X, point.Y, point.Z);
                    if (!PoliceNavigation.try_destination(officers[index]!.Movement, candidate, out Vector3 destination)) continue;
                    bool occupied = false;
                    for (int i = 0; i < officer_count; i++)
                    {
                        if (i == index || !searches[i].valid || searches[i].player_code != player.PlayerCode ||
                            Time.time >= searches[i].expires_seconds || !available(officers[i])) continue;
                        var other = officers[i]!.PursuitBehaviour;
                        if (!other.Active || other.TargetPlayer == null || other.TargetPlayer.PlayerCode != player.PlayerCode) continue;
                        if ((searches[i].destination - destination).sqrMagnitude < 2.25f) occupied = true;
                    }
                    if (occupied) continue;
                    cache.destination = destination;
                    cache.valid = true;
                    break;
                }
            }
            if (!cache.valid) return true;
            __result = cache.destination;
            return false;
        }
        catch (Exception error)
        {
            running = false;
            MelonLogger.Error($"Police: Disabled search coordination after a game API failure: {error}");
            return true;
        }
    }

    internal static void disable(string message)
    {
        running = false;
        MelonLogger.Error(message);
    }

    private static bool available(PoliceOfficer? officer)
    {
        return officer != null && officer.gameObject.activeInHierarchy && officer.IsConscious &&
            !officer.IsInVehicle && officer.PursuitBehaviour != null && officer.Movement != null;
    }

    internal static OfficerRole pursuit_role(PursuitBehaviour pursuit, int index)
    {
        if (index < 0 || index >= officer_count || !available(officers[index]) ||
            officers[index]!.PursuitBehaviour.Pointer != pursuit.Pointer || pursuit.TargetPlayer == null ||
            intercept_targets[index] != pursuit.TargetPlayer.PlayerCode || Time.time >= intercept_seconds[index])
            return OfficerRole.Chaser;
        return pursuit_roles[index];
    }

    private static void redirect_destination(NPCMovement __instance, ref Vector3 __0)
    {
        if (!running || !InstanceFinder.IsServer) return;
        try
        {
            int current_index = -1;
            PoliceOfficer? current = null;
            for (int i = 0; i < officer_count; i++)
            {
                PoliceOfficer? officer = officers[i];
                if (!available(officer) || officer!.Movement.Pointer != __instance.Pointer) continue;
                current = officer;
                current_index = i;
                break;
            }
            if (current == null) return;
            var pursuit = current.PursuitBehaviour;
            Player player = pursuit.TargetPlayer;
            if (!pursuit.Active || player == null) return;
            if (player.CrimeData == null || player.CrimeData.CurrentPursuitLevel == PlayerCrimeData.EPursuitLevel.None) return;
            bool sighted = pursuit.IsTargetImmediatelyVisible || read_pursuit(player).visible;
            if (!sighted)
            {
                intercept_valid[current_index] = false;
                intercept_seconds[current_index] = 0;
                redirect_search(pursuit, ref __0);
                return;
            }
            if (player.IsInVehicle || Time.time < redirects_resume_seconds[current_index]) return;
            Vector3 position = player.transform.position;
            if ((__0 - position).sqrMagnitude > 100f) return;
            if (Time.time < intercept_seconds[current_index])
            {
                if (intercept_valid[current_index] && intercept_targets[current_index] == player.PlayerCode &&
                    (intercept_origins[current_index] - __0).sqrMagnitude <= 4f)
                    __0 = intercept_positions[current_index];
                return;
            }
            intercept_valid[current_index] = false;
            intercept_seconds[current_index] = Time.time + 1f;
            Span<System.Numerics.Vector2> offsets = stackalloc System.Numerics.Vector2[officer_count];
            offsets.Fill(new System.Numerics.Vector2(float.NaN, float.NaN));
            var separation = System.Numerics.Vector2.Zero;
            Vector3 current_position = current.transform.position;
            for (int i = 0; i < officer_count; i++)
            {
                PoliceOfficer? other = officers[i];
                if (!available(other)) continue;
                var other_pursuit = other!.PursuitBehaviour;
                if (!other_pursuit.Active || other_pursuit.TargetPlayer == null ||
                    other_pursuit.TargetPlayer.PlayerCode != player.PlayerCode) continue;
                Vector3 relative_position = other.transform.position - position;
                if (Math.Abs(relative_position.y) > 2f) continue;
                offsets[i] = new System.Numerics.Vector2(relative_position.x, relative_position.z);
                Vector3 difference = current_position - other.transform.position;
                separation += PursuitSpacing.separation(new System.Numerics.Vector2(difference.x, difference.z), current_index, i);
            }
            Vector3 velocity = player.VelocityCalculator == null ? Vector3.zero : player.VelocityCalculator.Velocity;
            PursuitAssignment assignment = Interception.assign(offsets, current_index,
                new System.Numerics.Vector2(velocity.x, velocity.z));
            pursuit_roles[current_index] = assignment.role;
            intercept_targets[current_index] = player.PlayerCode;
            if (assignment.role == OfficerRole.Chaser) return;
            Vector3 relative = current_position - position;
            Vector3 destination = __0;
            if (assignment.redirect && (assignment.role != OfficerRole.Interceptor || (__0 - position).sqrMagnitude <= 16f))
            {
                destination = position + new Vector3(assignment.offset.X, 0, assignment.offset.Y);
            }
            else
            {
                var offset = PursuitSpacing.lateral_offset(separation, new System.Numerics.Vector2(-relative.x, -relative.z));
                if (offset.LengthSquared() < 0.0625f) return;
                destination += new Vector3(offset.X, 0, offset.Y);
            }
            if (!PoliceNavigation.try_redirect(__instance, __0, destination, out Vector3 resolved)) return;
            if (Math.Abs(resolved.y - position.y) > 2f) return;
            for (int i = 0; i < officer_count; i++)
            {
                if (i == current_index || !intercept_valid[i] || Time.time >= intercept_seconds[i] ||
                    intercept_targets[i] != player.PlayerCode) continue;
                if ((intercept_positions[i] - resolved).sqrMagnitude < 2.25f) return;
            }
            intercept_targets[current_index] = player.PlayerCode;
            intercept_origins[current_index] = __0;
            intercept_positions[current_index] = resolved;
            intercept_valid[current_index] = true;
            __0 = resolved;
        }
        catch (Exception error)
        {
            running = false;
            MelonLogger.Error($"Police: Disabled interception after a game API failure: {error}");
        }
    }
}
