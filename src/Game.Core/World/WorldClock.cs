// src/Game.Core/World/WorldClock.cs

using System;
using Microsoft.Xna.Framework;

namespace Game.Core.World;

/// <summary>
/// Turn-based world clock. Tracks time-of-day as a [0.0, 1.0) float
/// that advances by a fixed step each time the player takes an action.
///
/// The day is divided into five periods with smooth color transitions:
///
///   Night  [0.00 - 0.20]  deep indigo/black
///   Dawn   [0.20 - 0.30]  warm orange/rose
///   Day    [0.30 - 0.70]  bright white/sky blue
///   Dusk   [0.70 - 0.80]  amber/purple
///   Night  [0.80 - 1.00]  (wraps back to Night)
///
/// The ambient light color is a smooth lerp between adjacent period colors
/// so there are no hard jumps as periods transition.
///
/// Usage:
///   clock.Tick();                    // call once per player turn
///   Color ambient = clock.AmbientLight;
///   string period = clock.PeriodName;
/// </summary>
public sealed class WorldClock
{
    // -- Configuration ---------------------------------------------------

    /// <summary>
    /// How much TimeOfDay advances per player turn.
    /// Default: 1/240 means 240 turns = 1 full day cycle.
    /// Roughly: every move is ~6 minutes of in-game time.
    /// Tune freely.
    /// </summary>
    public float TurnsPerDay { get; set; } = 240f;

    // -- State -----------------------------------------------------------

    /// <summary>Current time as a fraction of the day. Range [0.0, 1.0).</summary>
    public float TimeOfDay { get; private set; } = 0.35f; // start at mid-morning

    /// <summary>Total turns elapsed since game start.</summary>
    public long TurnCount { get; private set; } = 0;

    // -- Period thresholds -----------------------------------------------

    private const float DawnStart = 0.20f;
    private const float DayStart = 0.30f;
    private const float DuskStart = 0.70f;
    private const float NightStart = 0.80f;

    // -- Ambient color keyframes -----------------------------------------
    // Each pair is: (periodStart, color) -- we lerp between adjacent keyframes.
    // XNA Color(r, g, b) -- alpha defaults to 255.

    private static readonly (float T, Color Color)[] Keyframes = new[]
    {
        (0.00f, new Color( 10,  12,  40)),  // Night -- deep indigo
        (0.20f, new Color( 10,  12,  40)),  // Night continues to Dawn start
        (0.25f, new Color(220, 120,  60)),  // Dawn peak -- warm amber/orange
        (0.30f, new Color(240, 220, 180)),  // Early day -- soft warm white
        (0.50f, new Color(220, 235, 255)),  // Midday -- bright sky white
        (0.70f, new Color(240, 200, 130)),  // Late afternoon -- golden
        (0.75f, new Color(180,  80,  60)),  // Dusk peak -- deep amber/rose
        (0.80f, new Color( 30,  20,  60)),  // Night begins -- dark purple
        (1.00f, new Color( 10,  12,  40)),  // Night -- same as 0.00 for wrap
    };

    // -- Events ----------------------------------------------------------

    /// <summary>Fired when the period changes (e.g. Day to Dusk).</summary>
    public event Action<TimePeriod, TimePeriod>? PeriodChanged;

    private TimePeriod _lastPeriod;

    // -- Construction ----------------------------------------------------

    public WorldClock()
    {
        _lastPeriod = GetPeriod();
    }

    // -- Tick ------------------------------------------------------------

    /// <summary>
    /// Advance the clock by one player turn.
    /// Call this from Game1 whenever <c>turnTaken == true</c> and the
    /// game is in Overworld mode.
    /// </summary>
    public void Tick()
    {
        TurnCount++;
        TimeOfDay += 1f / TurnsPerDay;
        if (TimeOfDay >= 1f)
            TimeOfDay -= 1f;

        var newPeriod = GetPeriod();
        if (newPeriod != _lastPeriod)
        {
            PeriodChanged?.Invoke(_lastPeriod, newPeriod);
            _lastPeriod = newPeriod;
        }
    }

    // -- Queries ---------------------------------------------------------

    /// <summary>Current ambient light color, smoothly interpolated between keyframes.</summary>
    public Color AmbientLight => SampleKeyframes(TimeOfDay);

    /// <summary>Human-readable name of the current period.</summary>
    public string PeriodName => GetPeriod() switch
    {
        TimePeriod.Dawn => "Dawn",
        TimePeriod.Day => "Day",
        TimePeriod.Dusk => "Dusk",
        TimePeriod.Night => "Night",
        _ => "Night"
    };

    /// <summary>Current time formatted as an in-game clock string (e.g. "14:32").</summary>
    public string TimeString
    {
        get
        {
            float hours = TimeOfDay * 24f;
            int h = (int)hours % 24;
            int m = (int)((hours - (int)hours) * 60f);
            return $"{h:D2}:{m:D2}";
        }
    }

    /// <summary>Is it currently nighttime? (Period == Night)</summary>
    public bool IsNight => GetPeriod() == TimePeriod.Night;

    /// <summary>
    /// A [0, 1] darkness factor. 0 = fully bright midday, 1 = pitch dark midnight.
    /// Useful for scaling enemy spawn tables or player visibility radius.
    /// </summary>
    public float DarknessFactor
    {
        get
        {
            // Derived from how dim the ambient light is vs full white
            var a = AmbientLight;
            float brightness = (a.R + a.G + a.B) / (3f * 255f);
            return 1f - brightness;
        }
    }

    // -- Private ---------------------------------------------------------

    public TimePeriod GetPeriod()
    {
        if (TimeOfDay >= NightStart || TimeOfDay < DawnStart) return TimePeriod.Night;
        if (TimeOfDay < DayStart) return TimePeriod.Dawn;
        if (TimeOfDay < DuskStart) return TimePeriod.Day;
        return TimePeriod.Dusk;
    }

    private static Color SampleKeyframes(float t)
    {
        // Find the two surrounding keyframes
        int upper = 0;
        for (int i = 1; i < Keyframes.Length; i++)
        {
            if (Keyframes[i].T >= t)
            {
                upper = i;
                break;
            }
        }

        int lower = Math.Max(0, upper - 1);

        var (t0, c0) = Keyframes[lower];
        var (t1, c1) = Keyframes[upper];

        if (Math.Abs(t1 - t0) < 0.0001f) return c0;

        float blend = (t - t0) / (t1 - t0);
        blend = Math.Clamp(blend, 0f, 1f);

        return new Color(
            (int)(c0.R + (c1.R - c0.R) * blend),
            (int)(c0.G + (c1.G - c0.G) * blend),
            (int)(c0.B + (c1.B - c0.B) * blend),
            255
        );
    }
}

/// <summary>Named time periods for event dispatching and log messages.</summary>
public enum TimePeriod
{
    Night,
    Dawn,
    Day,
    Dusk
}