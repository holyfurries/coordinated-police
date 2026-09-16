using System;
using System.Numerics;

namespace CoordinatedPolice;

internal enum ResponsePhase { Inactive, Responding, Pursuing, Searching }
internal enum DispatchSource { Nearby, Station }
internal enum DispatchPhase { Spacing, Break }

internal sealed class ResponseState
{
    public string player_code { get; private set; } = "";
    public ResponsePhase phase { get; private set; }
    public Vector3 last_known_position { get; private set; }
    public float search_started_seconds { get; private set; }
    public float dispatch_seconds { get; private set; }
    public int dispatch_count { get; private set; }
    public float started_seconds { get; private set; }
    public ResponseSeverity severity { get; private set; } = ResponseSeverity.Patrol;
    public ResponseRules rules => ResponseRules.for_severity(severity, player_count);
    private int player_count = 1;
    private bool urgent;
    private bool search_wave_sent;
    private readonly int[] attack_ids = new int[64];
    private int attack_ids_count;
    private int attack_index;
    private int lethal_attacks;
    private int burst_count;
    private DispatchPhase dispatch_phase;
    private int pending_count;
    private float pending_until_seconds;

    public void begin(string code, float now_seconds)
    {
        if (string.IsNullOrEmpty(code)) throw new ArgumentException("Player code is required.", nameof(code));
        if (!float.IsFinite(now_seconds)) throw new ArgumentOutOfRangeException(nameof(now_seconds));
        reset();
        player_code = code;
        phase = ResponsePhase.Responding;
        started_seconds = now_seconds;
        dispatch_seconds = now_seconds + 5f;
    }

    public void observe(bool visible, bool searching, Vector3 known_position, float now_seconds)
    {
        if (phase == ResponsePhase.Inactive || !float.IsFinite(now_seconds) || !float.IsFinite(known_position.LengthSquared())) return;
        if (urgent)
        {
            dispatch_seconds = Math.Min(dispatch_seconds, now_seconds + 1f);
            burst_count = 0;
            dispatch_phase = DispatchPhase.Spacing;
            urgent = false;
        }
        if (visible)
        {
            search_wave_sent = false;
            last_known_position = known_position;
            phase = ResponsePhase.Pursuing;
            return;
        }
        if (phase == ResponsePhase.Searching) return;
        if (phase != ResponsePhase.Pursuing && !searching) return;
        if (phase == ResponsePhase.Responding) last_known_position = known_position;
        phase = ResponsePhase.Searching;
        search_started_seconds = now_seconds;
    }

    public void observe_level(int pursuit_level)
    {
        raise_severity(pursuit_level switch
        {
            4 => ResponseSeverity.Armed,
            3 => ResponseSeverity.Pursuit,
            _ => ResponseSeverity.Patrol
        });
    }

    public void observe_crime(string crime_name)
    {
        raise_severity(ResponseRules.crime_severity(crime_name));
    }

    public bool record_attack(int attack_id, bool lethal)
    {
        if (phase == ResponsePhase.Inactive) return false;
        for (int i = 0; i < attack_ids_count; i++)
        {
            if (attack_ids[i] == attack_id) return false;
        }
        attack_ids[attack_index] = attack_id;
        attack_index = (attack_index + 1) % attack_ids.Length;
        attack_ids_count = Math.Min(attack_ids.Length, attack_ids_count + 1);
        if (lethal) lethal_attacks = Math.Min(3, lethal_attacks + 1);
        raise_severity(lethal_attacks >= 3 ? ResponseSeverity.Tactical : lethal ? ResponseSeverity.Armed : ResponseSeverity.Pursuit);
        return true;
    }

    public void record_kill()
    {
        raise_severity(ResponseSeverity.Tactical);
    }

    private void raise_severity(ResponseSeverity value)
    {
        if (phase == ResponsePhase.Inactive || value <= severity) return;
        severity = value;
        if (value >= ResponseSeverity.Armed) urgent = true;
    }

    public void set_player_count(int count)
    {
        player_count = Math.Clamp(count, 1, 4);
    }

    public bool can_send_search_wave(float now_seconds)
    {
        return phase == ResponsePhase.Searching && severity >= ResponseSeverity.Armed &&
            !search_wave_sent && now_seconds >= search_started_seconds && now_seconds - search_started_seconds <= 8f;
    }

    public int request_dispatch(float now_seconds, int active_count, int requested_count, DispatchSource source)
    {
        if (!float.IsFinite(now_seconds) || active_count < 0 || requested_count <= 0) return 0;
        bool search_wave = source == DispatchSource.Station && can_send_search_wave(now_seconds);
        if (phase == ResponsePhase.Inactive || (phase == ResponsePhase.Searching && !search_wave)) return 0;
        bool initial_response = source == DispatchSource.Station && phase == ResponsePhase.Responding && dispatch_count == 0;
        if (!initial_response && ((phase != ResponsePhase.Pursuing && !search_wave) || now_seconds < dispatch_seconds)) return 0;
        if (now_seconds >= pending_until_seconds) pending_count = 0;
        ResponseRules current = rules;
        if (dispatch_phase == DispatchPhase.Break) burst_count = 0;
        int capacity = Math.Max(0, current.active_limit - active_count - pending_count);
        int allowed = Math.Min(requested_count, Math.Min(current.dispatch_limit - dispatch_count,
            Math.Min(current.burst_limit - burst_count, capacity)));
        if (allowed == 0) return 0;
        if (search_wave) search_wave_sent = true;
        dispatch_count += allowed;
        burst_count += allowed;
        pending_count += allowed;
        pending_until_seconds = now_seconds + 30f;
        dispatch_phase = burst_count >= current.burst_limit ? DispatchPhase.Break : DispatchPhase.Spacing;
        dispatch_seconds = now_seconds + (dispatch_phase == DispatchPhase.Break ? current.break_seconds : current.interval_seconds);
        return allowed;
    }

    public void reset()
    {
        player_code = "";
        phase = ResponsePhase.Inactive;
        last_known_position = default;
        search_started_seconds = 0;
        started_seconds = 0;
        dispatch_seconds = 0;
        dispatch_count = 0;
        pending_count = 0;
        pending_until_seconds = 0;
        severity = ResponseSeverity.Patrol;
        player_count = 1;
        urgent = false;
        search_wave_sent = false;
        lethal_attacks = 0;
        attack_ids_count = 0;
        attack_index = 0;
        burst_count = 0;
        dispatch_phase = DispatchPhase.Spacing;
    }
}
