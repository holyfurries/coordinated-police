using System;
using Il2CppFishNet;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using MelonLoader;
using UnityEngine;

namespace CoordinatedPolice;

internal static class PolicePopulation
{
    private struct OfficerState
    {
        public PoliceOfficer? officer;
        public float defeated_seconds;
        public bool defeated;
        public bool owned_patrol;
        public bool auto_deactivate;
        public PatrolGroup? group;
        public PoliceStation? return_station;
        public float return_seconds;
        public int return_attempts;
    }
    private static readonly OfficerState[] states = new OfficerState[128];
    private static float next_seconds;
    private static float log_seconds;
    private static bool failed;

    public static void reset()
    {
        for (int i = 0; i < states.Length; i++)
        {
            ref OfficerState state = ref states[i];
            if (state.officer != null && state.owned_patrol) state.officer.AutoDeactivate = state.auto_deactivate;
        }
        Array.Clear(states, 0, states.Length);
        next_seconds = 0;
        log_seconds = 0;
        PoliceDistricts.reset();
        failed = false;
    }

    public static void tick()
    {
        if (failed || !LoadManager.InstanceExists || !LoadManager.Instance.IsGameLoaded) return;
        try
        {
            if (!InstanceFinder.IsServer)
            {
                reconcile_client();
                return;
            }
            if (Time.time < next_seconds) return;
            next_seconds = Time.time + 5f;
            int players = Math.Min(Player.PlayerList.Count, 16);
            if (players == 0) return;
            PopulationRules rules = PopulationRules.for_players(players);
            int count = Math.Min(PoliceOfficer.Officers.Count, states.Length);
            int patrol_count = 0;
            int defeated_count = 0;
            for (int i = 0; i < count; i++)
            {
                PoliceOfficer officer = PoliceOfficer.Officers[i];
                if (officer == null || officer.Health == null) continue;
                ref OfficerState state = ref get_state(officer);
                bool defeated = officer.Health.IsDead || officer.Health.IsKnockedOut;
                if (defeated && !state.defeated) state.defeated_seconds = Time.time;
                state.defeated = defeated;
                if (state.return_station != null)
                {
                    if (state.return_station.OfficerPool.Contains(officer))
                    {
                        MelonLogger.Msg($"Police: Reserve return confirmed officer={officer.ID}.");
                        state.return_station = null;
                    }
                    else if (defeated)
                    {
                        state.return_station = null;
                    }
                    else if (officer.PursuitTarget != null ||
                        (officer.PursuitBehaviour != null && officer.PursuitBehaviour.Active) ||
                        (officer.VehiclePursuitBehaviour != null && officer.VehiclePursuitBehaviour.Active) ||
                        (officer.FootPatrolBehaviour != null && officer.FootPatrolBehaviour.Active) ||
                        (officer.CheckpointBehaviour != null && officer.CheckpointBehaviour.Active) ||
                        (officer.SentryBehaviour != null && officer.SentryBehaviour.Active))
                    {
                        state.return_station = null;
                        MelonLogger.Msg($"Police: Returned officer already reassigned officer={officer.ID}.");
                    }
                    else if (Time.time >= state.return_seconds && player_distance_squared(officer.transform.position) >= 3600f)
                    {
                        if (state.return_attempts >= 3)
                            throw new InvalidOperationException($"Officer {officer.ID} did not return to the station pool after three attempts.");
                        officer.Deactivate();
                        state.return_attempts++;
                        state.return_seconds = Time.time + 5f;
                    }
                }
                if (state.owned_patrol && !defeated && state.group != null &&
                    officer.FootPatrolBehaviour != null && officer.FootPatrolBehaviour.Active && state.group.IsGroupReadyToAdvance())
                    state.group.AdvanceGroup();
                if (defeated) defeated_count++;
                else if (officer.FootPatrolBehaviour != null && officer.FootPatrolBehaviour.Active && !officer.isInBuilding)
                    patrol_count++;
            }
            int reserve_count = count_reserves();
            int replenished = 0;
            for (int i = 0; i < states.Length && replenished < 2 && reserve_count < rules.reserve_target; i++)
            {
                ref OfficerState state = ref states[i];
                PoliceOfficer? officer = state.officer;
                if (officer == null || !state.defeated || state.return_station != null || officer.Movement == null || officer.IsInVehicle || officer.AssignedVehicle != null) continue;
                PoliceStation station = PoliceStation.GetClosestPoliceStation(officer.transform.position);
                if (station == null || station.SpawnPoint == null) continue;
                if (!PopulationRules.can_replenish(state.defeated_seconds, Time.time,
                    player_distance_squared(officer.transform.position), player_distance_squared(station.SpawnPoint.position))) continue;
                if (officer.FootPatrolBehaviour != null) officer.FootPatrolBehaviour.Disable_Networked(null);
                if (officer.PursuitBehaviour != null) officer.PursuitBehaviour.Disable_Networked(null);
                if (officer.VehiclePatrolBehaviour != null) officer.VehiclePatrolBehaviour.Disable_Networked(null);
                if (officer.BodySearchBehaviour != null) officer.BodySearchBehaviour.Disable_Networked(null);
                if (officer.CheckpointBehaviour != null) officer.CheckpointBehaviour.Disable_Networked(null);
                if (officer.SentryBehaviour != null) officer.SentryBehaviour.Disable_Networked(null);
                officer.SetVisible(false, true);
                officer.Movement.Warp(station.SpawnPoint.position);
                officer.Health.SetAfflictedWithLethalEffect(false);
                officer.Health.Revive();
                officer.Health.RestoreHealth();
                if (state.owned_patrol) officer.AutoDeactivate = state.auto_deactivate;
                officer.Deactivate();
                state.return_station = station.OfficerPool.Contains(officer) ? null : station;
                state.return_seconds = Time.time + 5f;
                state.return_attempts = 1;
                state.owned_patrol = false;
                state.group = null;
                state.defeated = false;
                replenished++;
                reserve_count = count_reserves();
                MelonLogger.Msg($"Police: Reserve replacement requested officer={officer.ID}, pooled={station.OfficerPool.Contains(officer)}, reserve={reserve_count}/{rules.reserve_target}.");
            }
            bool wanted = false;
            for (int i = 0; i < players; i++)
            {
                Player player = Player.PlayerList[i];
                if (player != null && player.CrimeData != null && player.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None)
                    wanted = true;
            }
            bool districts_ready = PoliceDistricts.prepare(rules.patrol_target);
            if (!wanted && districts_ready && patrol_count < rules.patrol_target)
                deploy_patrols(rules, patrol_count, reserve_count);
            else if (!wanted && districts_ready && patrol_count == rules.patrol_target)
                rebalance_patrol();
            if (!wanted && patrol_count > rules.patrol_target)
                return_patrols(patrol_count - rules.patrol_target);
            if (Time.time < log_seconds) return;
            log_seconds = Time.time + 30f;
            reserve_count = count_reserves();
            patrol_count = 0;
            defeated_count = 0;
            for (int i = 0; i < count; i++)
            {
                PoliceOfficer officer = PoliceOfficer.Officers[i];
                if (officer == null || officer.Health == null) continue;
                if (officer.Health.IsDead || officer.Health.IsKnockedOut) defeated_count++;
                else if (officer.FootPatrolBehaviour != null && officer.FootPatrolBehaviour.Active && !officer.isInBuilding)
                    patrol_count++;
            }
            if (districts_ready) PoliceDistricts.log();
            MelonLogger.Msg($"Police: Population players={players}, foot_patrols={patrol_count}/{rules.patrol_target}, reserves={reserve_count}/{rules.reserve_target}, defeated={defeated_count}, registered={count}, patrol_staffing_paused={wanted}. Targets depend on native officer availability and safe replacement locations.");
        }
        catch (Exception error)
        {
            failed = true;
            MelonLogger.Error($"Police: Population management disabled for this scene: {error}");
        }
    }

    private static ref OfficerState get_state(PoliceOfficer officer)
    {
        int empty_index = -1;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].officer == officer) return ref states[i];
            if (states[i].officer == null && empty_index < 0) empty_index = i;
        }
        if (empty_index < 0) throw new InvalidOperationException("Police population tracking capacity exhausted.");
        states[empty_index] = new OfficerState { officer = officer };
        return ref states[empty_index];
    }

    private static int count_reserves()
    {
        int total = 0;
        int count = Math.Min(PoliceStation.PoliceStations.Count, 16);
        for (int i = 0; i < count; i++)
        {
            PoliceStation station = PoliceStation.PoliceStations[i];
            if (station == null) continue;
            int officers = Math.Min(station.OfficerPool.Count, 128);
            for (int j = 0; j < officers; j++)
            {
                PoliceOfficer officer = station.OfficerPool[j];
                if (officer != null && officer.IsConscious) total++;
            }
        }
        return total;
    }

    private static float player_distance_squared(Vector3 position)
    {
        float nearest = float.MaxValue;
        int count = Math.Min(Player.PlayerList.Count, 16);
        if (count == 0 || count != Player.PlayerList.Count) return 0f;
        for (int i = 0; i < count; i++)
        {
            Player player = Player.PlayerList[i];
            if (player == null) return 0f;
            float distance = (player.transform.position - position).sqrMagnitude;
            if (!float.IsFinite(distance)) return 0f;
            nearest = Math.Min(nearest, distance);
        }
        return nearest;
    }

    private static void deploy_patrols(PopulationRules rules, int patrol_count, int reserve_count)
    {
        int reserve_floor = Math.Max(4, rules.reserve_target / 2);
        if (reserve_count <= reserve_floor || patrol_count >= rules.patrol_target) return;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            FootPatrolRoute? route = PoliceDistricts.next_route(out int district, out int route_members);
            if (route == null) return;
            int waypoint_count = Math.Min(route.Waypoints.Length, 128);
            int waypoint_index = (Math.Clamp(route.StartWaypointIndex, 0, waypoint_count - 1) + route_members) % waypoint_count;
            Transform waypoint = route.Waypoints[waypoint_index];
            if (waypoint == null) continue;
            PoliceStation station = PoliceStation.GetClosestPoliceStation(waypoint.position);
            if (station == null || station.SpawnPoint == null || station.OfficerPool.Count == 0) continue;
            if (player_distance_squared(station.SpawnPoint.position) < 3600f) continue;
            PoliceOfficer officer = station.PullOfficer();
            if (officer == null) continue;
            if (!officer.IsConscious || officer.Movement == null)
                throw new InvalidOperationException("Station supplied an unavailable patrol officer.");
            ref OfficerState state = ref get_state(officer);
            if (!state.owned_patrol) state.auto_deactivate = officer.AutoDeactivate;
            state.owned_patrol = true;
            state.group = new PatrolGroup(route) { CurrentWaypoint = waypoint_index };
            officer.AutoDeactivate = false;
            if (officer.CurrentBuilding != null) officer.ExitBuilding(officer.CurrentBuilding);
            officer.Activate();
            officer.Movement.Warp(station.SpawnPoint.position);
            officer.StartFootPatrol(state.group, false);
            patrol_count++;
            PoliceDistricts.actual[district]++;
            MelonLogger.Msg($"Police: Patrol assigned officer={officer.ID}, district={(EMapRegion)district}, route={route.RouteName}, waypoint={state.group.CurrentWaypoint}, foot_patrols={patrol_count}/{rules.patrol_target}. Departing station without route-start warp.");
            return;
        }
    }

    private static void rebalance_patrol()
    {
        FootPatrolRoute? route = PoliceDistricts.next_route(out int district, out int route_members);
        if (route == null) return;
        int waypoint_count = Math.Min(route.Waypoints.Length, 128);
        int waypoint_index = (Math.Clamp(route.StartWaypointIndex, 0, waypoint_count - 1) + route_members) % waypoint_count;
        Transform waypoint = route.Waypoints[waypoint_index];
        if (waypoint == null) return;
        int path_checks = 0;
        for (int i = 0; i < states.Length && path_checks < 2; i++)
        {
            ref OfficerState state = ref states[i];
            PoliceOfficer? officer = state.officer;
            if (!state.owned_patrol || officer == null || !officer.IsConscious || officer.PursuitTarget != null ||
                officer.IsInVehicle || officer.AssignedVehicle != null || officer.Movement == null ||
                officer.FootPatrolBehaviour == null || !officer.FootPatrolBehaviour.Active ||
                officer.isInBuilding || player_distance_squared(officer.transform.position) < 3600f ||
                (officer.PursuitBehaviour != null && officer.PursuitBehaviour.Active) ||
                (officer.VehiclePursuitBehaviour != null && officer.VehiclePursuitBehaviour.Active) ||
                (officer.BodySearchBehaviour != null && officer.BodySearchBehaviour.Active) ||
                (officer.CheckpointBehaviour != null && officer.CheckpointBehaviour.Active) ||
                (officer.SentryBehaviour != null && officer.SentryBehaviour.Active)) continue;
            PatrolGroup group = officer.FootPatrolBehaviour.Group;
            if (group == null || group.Route == null) continue;
            int previous = PoliceDistricts.region_for_route(group.Route);
            if (previous < 0 || PoliceDistricts.actual[previous] <= PoliceDistricts.targets[previous]) continue;
            path_checks++;
            if (!PoliceNavigation.try_destination(officer.Movement, waypoint.position, out _)) continue;
            officer.FootPatrolBehaviour.Disable_Networked(null);
            state.group = new PatrolGroup(route) { CurrentWaypoint = waypoint_index };
            officer.StartFootPatrol(state.group, false);
            PoliceDistricts.actual[previous]--;
            PoliceDistricts.actual[district]++;
            MelonLogger.Msg($"Police: Patrol reassigned officer={officer.ID}, from={(EMapRegion)previous}, to={(EMapRegion)district}, route={route.RouteName}. Walking to new route.");
            return;
        }
    }

    private static void return_patrols(int excess_count)
    {
        for (int i = 0; i < states.Length && excess_count > 0; i++)
        {
            ref OfficerState state = ref states[i];
            PoliceOfficer? officer = state.officer;
            if (!state.owned_patrol || officer == null || !officer.IsConscious || officer.PursuitTarget != null ||
                officer.IsInVehicle || officer.AssignedVehicle != null || officer.FootPatrolBehaviour == null ||
                !officer.FootPatrolBehaviour.Active || player_distance_squared(officer.transform.position) < 3600f) continue;
            officer.FootPatrolBehaviour.Disable_Networked(null);
            officer.AutoDeactivate = state.auto_deactivate;
            officer.Deactivate();
            state.owned_patrol = false;
            state.group = null;
            excess_count--;
            MelonLogger.Msg($"Police: Extra patrol returned officer={officer.ID}.");
        }
    }

    private static void reconcile_client()
    {
        int count = Math.Min(PoliceOfficer.Officers.Count, 128);
        for (int i = 0; i < count; i++)
        {
            PoliceOfficer officer = PoliceOfficer.Officers[i];
            if (officer == null || officer.Health == null || officer.Behaviour == null) continue;
            var health = officer.Health;
            if ((!health.IsDead && !health.IsKnockedOut) || !float.IsFinite(health.Health) ||
                !float.IsFinite(health.MaxHealth) || health.Health < health.MaxHealth || health.MaxHealth <= 0) continue;
            if (officer.Behaviour.DeadBehaviour == null || officer.Behaviour.UnconsciousBehaviour == null ||
                officer.Behaviour.DeadBehaviour.Enabled || officer.Behaviour.UnconsciousBehaviour.Enabled) continue;
            health.Revive();
            MelonLogger.Msg($"Police: Client reconciled restored officer={officer.ID} from host health and behaviour state.");
        }
    }
}
