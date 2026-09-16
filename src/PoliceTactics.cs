using System;
using HarmonyLib;
using Il2CppScheduleOne.Combat;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using MelonLoader;
using UnityEngine;

namespace CoordinatedPolice;

internal static class PoliceTactics
{
    private const string shotgun_path = "Avatar/Equippables/PumpShotgun";
    private static bool? shotgun_available;
    private readonly record struct ImpactEvent(ResponseState? response, int attack_id, bool lethal, float health_before);

    public static void install(HarmonyLib.Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(CombatBehaviour), nameof(CombatBehaviour.SetWeapon)),
            prefix: new HarmonyMethod(typeof(PoliceTactics), nameof(vary_weapon)));
        harmony.Patch(AccessTools.Method(typeof(PursuitBehaviour), nameof(PursuitBehaviour.GetIdealRangedWeaponDistance)),
            postfix: new HarmonyMethod(typeof(PoliceTactics), nameof(set_firing_distance)));
        harmony.Patch(AccessTools.Method(typeof(PlayerCrimeData), nameof(PlayerCrimeData.AddCrime)),
            postfix: new HarmonyMethod(typeof(PoliceTactics), nameof(record_crime)));
        harmony.Patch(AccessTools.Method(typeof(NPC), nameof(NPC.RpcLogic___ReceiveImpact_427288424)),
            prefix: new HarmonyMethod(typeof(PoliceTactics), nameof(record_impact_before)),
            postfix: new HarmonyMethod(typeof(PoliceTactics), nameof(record_impact_after)));
    }

    public static void reset()
    {
        shotgun_available = null;
    }

    private static bool try_context(CombatBehaviour combat, out PursuitBehaviour? pursuit,
        out ResponseState? response, out int officer_index)
    {
        pursuit = null;
        response = null;
        officer_index = -1;
        if (!Main.authoritative) return false;
        pursuit = combat.TryCast<PursuitBehaviour>();
        if (pursuit == null || !pursuit.Active || pursuit.officer == null || pursuit.TargetPlayer == null) return false;
        Player player = pursuit.TargetPlayer;
        if (player.CrimeData == null || player.CrimeData.CurrentPursuitLevel == PlayerCrimeData.EPursuitLevel.None) return false;
        officer_index = PoliceOfficer.Officers.IndexOf(pursuit.officer);
        if (officer_index < 0 || officer_index >= 128) return false;
        response = Main.get_response(player.PlayerCode, create: true);
        if (response == null) return false;
        response.observe_level((int)player.CrimeData.CurrentPursuitLevel);
        return true;
    }

    private static void vary_weapon(CombatBehaviour __instance, ref string __0)
    {
        try
        {
            if (!try_context(__instance, out var pursuit, out var response, out int index)) return;
            string? gun = pursuit!.Weapon_Gun?.AssetPath;
            string? taser = pursuit.Weapon_Taser?.AssetPath;
            string? baton = pursuit.Weapon_Baton?.AssetPath;
            if (__0 != gun && __0 != taser && __0 != baton) return;
            OfficerWeapon choice = ResponseRules.weapon(response!.severity,
                (int)pursuit.TargetPlayer.CrimeData.CurrentPursuitLevel, index);
            if (choice == OfficerWeapon.Shotgun && shotgun_available == null)
            {
                shotgun_available = Resources.Load<UnityEngine.Object>(shotgun_path) != null;
                if (shotgun_available == false) MelonLogger.Warning("Police: Shotgun asset unavailable; using standard police guns.");
            }
            string? path = choice switch
            {
                OfficerWeapon.Baton => baton,
                OfficerWeapon.Taser => taser,
                OfficerWeapon.Pistol => gun,
                OfficerWeapon.Shotgun => shotgun_available == true ? shotgun_path : gun,
                _ => null
            };
            if (!string.IsNullOrEmpty(path)) __0 = path;
        }
        catch (Exception error)
        {
            Main.disable($"Police: Weapon selection failed: {error}");
        }
    }

    private static void set_firing_distance(PursuitBehaviour __instance, ref float __result)
    {
        try
        {
            if (!try_context(__instance, out var pursuit, out _, out int index)) return;
            var weapon = pursuit!.currentWeapon;
            if (weapon == null || string.IsNullOrEmpty(weapon.AssetPath)) return;
            OfficerWeapon kind = weapon.AssetPath == shotgun_path ? OfficerWeapon.Shotgun :
                weapon.AssetPath == pursuit.Weapon_Gun?.AssetPath ? OfficerWeapon.Pistol : OfficerWeapon.Native;
            float distance = ResponseRules.firing_distance(ResponseRules.role(index), kind);
            float maximum = weapon.MaxUseRange * 0.8f;
            float minimum = Math.Max(0f, weapon.MinUseRange);
            if (distance <= 0f || !float.IsFinite(minimum) || !float.IsFinite(maximum) || maximum <= minimum) return;
            __result = Math.Clamp(distance, minimum, maximum);
        }
        catch (Exception error)
        {
            Main.disable($"Police: Combat spacing failed: {error}");
        }
    }

    private static void record_crime(PlayerCrimeData __instance, Crime __0, int __1)
    {
        if (!Main.authoritative || __0 == null || __1 <= 0 || __instance.Player == null) return;
        try
        {
            Main.get_response(__instance.Player.PlayerCode, create: true)?.observe_crime(__0.GetIl2CppType().Name);
        }
        catch (Exception error)
        {
            Main.disable($"Police: Crime severity update failed: {error}");
        }
    }

    private static void record_impact_before(NPC __instance, Impact __0, out ImpactEvent __state)
    {
        __state = default;
        if (!Main.authoritative) return;
        try
        {
            PoliceOfficer? officer = __instance.TryCast<PoliceOfficer>();
            if (officer == null || officer.Health == null || officer.Health.IsDead ||
                __0.ImpactDamage <= 0f || __0.ImpactSource == null) return;
            Player player = __0.ImpactSource.GetComponent<Player>();
            if (player == null) return;
            ResponseState? response = Main.get_response(player.PlayerCode, create: true);
            bool lethal = __0.ImpactType is EImpactType.Bullet or EImpactType.SharpMetal or EImpactType.Explosion;
            __state = new ImpactEvent(response, __0.ImpactID, lethal, officer.Health.Health);
        }
        catch (Exception error)
        {
            Main.disable($"Police: Attack attribution failed: {error}");
        }
    }

    private static void record_impact_after(NPC __instance, ImpactEvent __state)
    {
        if (!Main.authoritative || __state.response == null) return;
        try
        {
            var health = __instance.Health;
            if (health == null || (!health.IsDead && health.Health >= __state.health_before)) return;
            if (!__state.response.record_attack(__state.attack_id, __state.lethal)) return;
            if (health.IsDead) __state.response.record_kill();
        }
        catch (Exception error)
        {
            Main.disable($"Police: Attack severity update failed: {error}");
        }
    }
}
