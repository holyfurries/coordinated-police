using MelonLoader;

namespace CoordinatedPolice;

internal static class PoliceConfiguration
{
    public static void load()
    {
        MelonPreferences_Category category = MelonPreferences.CreateCategory("CoordinatedPolice");
        var patrols = category.CreateEntry("patrols_per_player", 5, "Foot patrols per connected player (0-16, up to four players)");
        var reserves = category.CreateEntry("reserve_base", 8, "Solo ready reserve target (0-64)");
        var extra_reserves = category.CreateEntry("reserves_per_extra_player", 4, "Extra reserves per additional player (0-16)");
        var response = category.CreateEntry("response_size_multiplier", 1f, "Response limits and burst size multiplier (0.25-2)");
        var timing = category.CreateEntry("reinforcement_time_multiplier", 1f, "Dispatch delays and breaks multiplier (0.25-4; lower is faster)");
        var north = category.CreateEntry("northtown_weight", 1, "Northtown patrol share (0-10)");
        var west = category.CreateEntry("westville_weight", 1, "Westville patrol share (0-10)");
        var downtown = category.CreateEntry("downtown_weight", 1, "Downtown patrol share (0-10)");
        var docks = category.CreateEntry("docks_weight", 1, "Docks patrol share (0-10)");
        var suburbia = category.CreateEntry("suburbia_weight", 1, "Suburbia patrol share (0-10)");
        var uptown = category.CreateEntry("uptown_weight", 1, "Uptown patrol share (0-10)");
        var requested = new PoliceSettings
        {
            patrols_per_player = patrols.Value,
            reserve_base = reserves.Value,
            reserves_per_extra_player = extra_reserves.Value,
            response_size = response.Value,
            reinforcement_time = timing.Value,
            northtown_weight = north.Value,
            westville_weight = west.Value,
            downtown_weight = downtown.Value,
            docks_weight = docks.Value,
            suburbia_weight = suburbia.Value,
            uptown_weight = uptown.Value
        };
        PoliceSettings settings = requested.validated();
        PoliceSettings.current = settings;
        if (requested != settings) MelonLogger.Warning("Police: Out-of-range settings were clamped; non-finite multipliers use 1.");
        patrols.Value = settings.patrols_per_player;
        reserves.Value = settings.reserve_base;
        extra_reserves.Value = settings.reserves_per_extra_player;
        response.Value = settings.response_size;
        timing.Value = settings.reinforcement_time;
        north.Value = settings.northtown_weight;
        west.Value = settings.westville_weight;
        downtown.Value = settings.downtown_weight;
        docks.Value = settings.docks_weight;
        suburbia.Value = settings.suburbia_weight;
        uptown.Value = settings.uptown_weight;
        category.SaveToFile(false);
        MelonLogger.Msg($"Police: Settings patrols_per_player={settings.patrols_per_player}, reserves={settings.reserve_base}+{settings.reserves_per_extra_player}/extra player, response_size={settings.response_size}, reinforcement_time={settings.reinforcement_time}. Host settings govern gameplay; restart to apply edits.");
    }
}
