// src/Game.Client/Rendering/TorchFlicker.cs

using System;
using System.Collections.Generic;
using Game.Core.Lighting;

#nullable enable

namespace Game.Client.Rendering;

/// <summary>
/// Animates flickering light sources in real time using layered sine waves.
///
/// Even in a turn-based game, visual flicker runs every frame — it's purely
/// cosmetic and does NOT affect gameplay (FOV, attack range, etc.).
///
/// Each flickering LightSource gets an independent noise channel so torches
/// in the same room don't pulse in unison, which looks mechanical.
///
/// Usage (in TileRenderer or Game1.Draw):
///   _flicker.Update(gameTime.ElapsedGameTime.TotalSeconds);
///   float[] intensities = _flicker.GetIntensities(sources);
///   _lightMap.Recompute(sources, intensities);   // re-tint per-frame
///   _renderer.Draw(state, camera, _lightMap, ...);
///
/// Note: Recomputing the LightMap every frame is cheap for dungeon-scale
/// maps (60×40). If you later move to large maps, gate this to only run
/// when a flicker channel crosses a "changed enough" threshold.
/// </summary>
public sealed class TorchFlicker
{
    // ── Per-source flicker state ────────────────────────────────────

    private readonly struct FlickerState
    {
        // Primary wave
        public float Phase1 { get; init; }
        public float Speed1 { get; init; }
        public float Amplitude1 { get; init; }

        // Secondary wave (higher freq, lower amplitude)
        public float Phase2 { get; init; }
        public float Speed2 { get; init; }
        public float Amplitude2 { get; init; }
    }

    // ── State ───────────────────────────────────────────────────────

    private readonly List<FlickerState> _states = new();
    private double _elapsed; // seconds since start

    private readonly Random _rng;

    // ── Construction ────────────────────────────────────────────────

    public TorchFlicker(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    // ── Per-frame update ────────────────────────────────────────────

    /// <summary>Advance the flicker simulation. Call once per Draw frame.</summary>
    public void Update(double deltaSeconds)
    {
        _elapsed += deltaSeconds;
    }

    // ── Output ──────────────────────────────────────────────────────

    /// <summary>
    /// Return a per-source intensity array for use in LightMap.Recompute().
    ///
    /// The array is reused internally — do not store it; use it immediately.
    /// Non-flickering sources get their base Intensity unchanged.
    /// </summary>
    public float[] GetIntensities(IReadOnlyList<LightSource> sources)
    {
        // Ensure we have a flicker state for every source
        while (_states.Count < sources.Count)
            _states.Add(CreateFlickerState());

        // Reuse a shared array — caller uses it immediately, no aliasing risk
        if (_intensityBuffer == null || _intensityBuffer.Length < sources.Count)
            _intensityBuffer = new float[Math.Max(sources.Count, 16)];

        float t = (float)_elapsed;

        for (int i = 0; i < sources.Count; i++)
        {
            var src = sources[i];

            if (!src.Flickers)
            {
                _intensityBuffer[i] = src.Intensity;
                continue;
            }

            var fs = _states[i];

            // Two-layer sine: primary slow roll + secondary fast crackle
            float wave1 = MathF.Sin(t * fs.Speed1 + fs.Phase1) * fs.Amplitude1;
            float wave2 = MathF.Sin(t * fs.Speed2 + fs.Phase2) * fs.Amplitude2;

            // Combine and clamp to [0.2, 1.0] so torches never go fully dark
            float intensity = Math.Clamp(src.Intensity + wave1 + wave2, 0.2f, 1.0f);
            _intensityBuffer[i] = intensity;
        }

        return _intensityBuffer;
    }

    // ── Private ─────────────────────────────────────────────────────

    private float[]? _intensityBuffer;

    private FlickerState CreateFlickerState()
    {
        return new FlickerState
        {
            Phase1 = (float)(_rng.NextDouble() * MathF.PI * 2),
            Speed1 = 1.5f + (float)_rng.NextDouble() * 1.5f,   // 1.5–3 Hz
            Amplitude1 = 0.08f + (float)_rng.NextDouble() * 0.10f, // ±8–18%

            Phase2 = (float)(_rng.NextDouble() * MathF.PI * 2),
            Speed2 = 5f + (float)_rng.NextDouble() * 5f,     // 5–10 Hz
            Amplitude2 = 0.02f + (float)_rng.NextDouble() * 0.04f, // ±2–6%
        };
    }
}