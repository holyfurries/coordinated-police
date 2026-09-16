using System;
using System.Numerics;

namespace CoordinatedPolice;

internal sealed class StuckRecovery
{
    private Vector3? anchor;
    private float progress_seconds;
    private int attempts;

    public bool try_repath(float now_seconds, Vector3 position, bool eligible)
    {
        if (!eligible || !float.IsFinite(now_seconds) || !float.IsFinite(position.LengthSquared()))
        {
            reset();
            return false;
        }
        if (anchor == null || Vector3.DistanceSquared(anchor.Value, position) >= 0.25f || now_seconds < progress_seconds)
        {
            anchor = position;
            progress_seconds = now_seconds;
            attempts = 0;
            return false;
        }
        if (now_seconds - progress_seconds < 6f || attempts >= 3) return false;
        attempts++;
        progress_seconds = now_seconds;
        return true;
    }

    public void reset()
    {
        anchor = null;
        progress_seconds = 0;
        attempts = 0;
    }
}
