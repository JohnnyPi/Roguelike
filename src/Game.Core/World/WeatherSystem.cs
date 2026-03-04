// src/Game.Core/World/WeatherSystem.cs

using System;
using Microsoft.Xna.Framework;

namespace Game.Core.World;

/// <summary>
/// Turn-based weather simulation for the overworld.
///
/// Weather state transitions fire based on turn counts and a weighted
/// probability table that accounts for time-of-day. Storms are more
/// likely at dusk; fog favors night/dawn; clear skies favor midday.
///
/// Weather modifies two things the renderer cares about:
///   1. LightMultiplier  -- multiplied against WorldClock.AmbientLight
///   2. OverlayTint      -- full-screen color tint drawn over the scene
///                         (semi-transparent; fog = grey, rain = blue-grey)
///
/// Usage:
///   weather.Tick(clock.TimeOfDay);       // once per player turn
///   Color ambient = clock.AmbientLight * weather.LightMultiplier
///   // ...then draw weather.OverlayTint as a fullscreen alpha overlay
/// </summary>
public sealed class WeatherSystem
{
    // -- State -----------------------------------------------------------

    public WeatherState Current { get; private set; } = WeatherState.Clear;

    /// <summary>Turns remaining before the next weather check.</summary>
    private int _turnsUntilCheck = 40;

    private readonly Random _rng;

    // -- Events ----------------------------------------------------------

    /// <summary>Fired when weather changes. Args: (old, new).</summary>
    public event Action<WeatherState, WeatherState>? WeatherChanged;

    // -- Construction ----------------------------------------------------

    public WeatherSystem(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    // -- Tick ------------------------------------------------------------

    /// <summary>
    /// Advance weather by one turn.
    /// <paramref name="timeOfDay"/> is the current WorldClock.TimeOfDay [0, 1).
    /// </summary>
    public void Tick(float timeOfDay)
    {
        _turnsUntilCheck--;
        if (_turnsUntilCheck > 0) return;

        // Randomize next check interval: 30-80 turns
        _turnsUntilCheck = _rng.Next(30, 80);

        TryTransition(timeOfDay);
    }

    // -- Derived rendering properties ------------------------------------

    /// <summary>
    /// Multiply WorldClock.AmbientLight by this before passing to LightMap.
    /// Overcast and stormy conditions dim the ambient.
    /// Note: XNA Color(r, g, b, a) -- alpha is the 4th argument.
    /// </summary>
    public Color LightMultiplier => Current switch
    {
        WeatherState.Clear => Color.White,
        WeatherState.Overcast => new Color(170, 175, 190, 255),  // ~67% dim, slight blue
        WeatherState.Rain => new Color(140, 150, 170, 255),  // ~55% dim, grey-blue
        WeatherState.Storm => new Color(100, 105, 130, 255),  // ~40% dim, dark grey
        WeatherState.Fog => new Color(180, 185, 190, 255),  // ~70% dim, flat grey
        _ => Color.White
    };

    /// <summary>
    /// Full-screen overlay tint.
    /// TileRenderer draws a screen-filling semi-transparent rectangle
    /// using this color after drawing tiles/entities.
    /// Alpha=0 means no overlay.
    /// Note: XNA Color(r, g, b, a) -- alpha is the 4th argument.
    /// </summary>
    public Color OverlayTint => Current switch
    {
        WeatherState.Clear => Color.Transparent,
        WeatherState.Overcast => new Color(80, 85, 100, 25),
        WeatherState.Rain => new Color(50, 60, 90, 55),
        WeatherState.Storm => new Color(30, 35, 60, 90),
        WeatherState.Fog => new Color(200, 205, 210, 80),
        _ => Color.Transparent
    };

    /// <summary>
    /// Fog-of-war radius penalty from weather.
    /// Storm/fog reduce how far the player can see.
    /// </summary>
    public int VisibilityPenalty => Current switch
    {
        WeatherState.Rain => 1,
        WeatherState.Storm => 3,
        WeatherState.Fog => 4,
        _ => 0
    };

    /// <summary>Human-readable weather name for HUD/log messages.</summary>
    public string Name => Current switch
    {
        WeatherState.Clear => "Clear",
        WeatherState.Overcast => "Overcast",
        WeatherState.Rain => "Rainy",
        WeatherState.Storm => "Stormy",
        WeatherState.Fog => "Foggy",
        _ => "Clear"
    };

    // -- Transition logic ------------------------------------------------

    /// <summary>
    /// Weighted transition table.
    /// Weights vary by time of day to make weather feel natural.
    /// Storms cluster at dusk, fog at night/dawn, clear at midday.
    /// </summary>
    private void TryTransition(float timeOfDay)
    {
        bool isNight = timeOfDay >= 0.80f || timeOfDay < 0.20f;
        bool isDawn = timeOfDay >= 0.20f && timeOfDay < 0.30f;
        bool isDusk = timeOfDay >= 0.70f && timeOfDay < 0.80f;
        bool isMidday = timeOfDay >= 0.40f && timeOfDay < 0.60f;

        // Build a weighted candidate list given the current weather + time of day.
        // (state, weight)
        var candidates = new (WeatherState State, int Weight)[]
        {
            (WeatherState.Clear,
                isMidday ? 60 : (isNight ? 20 : 35)),

            (WeatherState.Overcast,
                isDawn || isDusk ? 30 : 20),

            (WeatherState.Rain,
                isDusk ? 25 : (isMidday ? 8 : 15)),

            (WeatherState.Storm,
                isDusk ? 20 : (isNight ? 10 : 5)),

            (WeatherState.Fog,
                isNight || isDawn ? 25 : (isMidday ? 3 : 10)),
        };

        // Weighted random pick
        int total = 0;
        foreach (var (_, w) in candidates) total += w;

        int roll = _rng.Next(total);
        int cumulative = 0;
        WeatherState next = Current;

        foreach (var (state, weight) in candidates)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                next = state;
                break;
            }
        }

        if (next != Current)
        {
            var old = Current;
            Current = next;
            WeatherChanged?.Invoke(old, next);
        }
    }
}

/// <summary>Weather states affecting ambient lighting and FOV radius.</summary>
public enum WeatherState
{
    Clear,
    Overcast,
    Rain,
    Storm,
    Fog
}