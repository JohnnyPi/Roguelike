// src/Game.Client/Input/InputBindings.cs
//
// Data class mapping GameAction values to one or more keyboard Keys.
// Mouse bindings (PathPoint, PathConfirm) are handled via MouseButton
// in MouseInputHandler; they are not listed here.
//
// Loading priority (highest to lowest):
//   1. User config file  (content/BasePack/config/controls.yml)
//   2. Compile-time defaults via Defaults()
//
// Usage:
//   var bindings = InputBindings.Load(controlsYamlPath);
//   if (bindings.IsPressed(GameAction.MoveNorth, currentKb, prevKb)) ...
//
// Rebinding at runtime:
//   bindings.Bind(GameAction.MoveNorth, Keys.W, Keys.Up);

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Game.Client.Input;

public sealed class InputBindings
{
    // ── Internal storage ──────────────────────────────────────────
    // Each action maps to an ordered list of keys (any one triggers it).
    private readonly Dictionary<GameAction, List<Keys>> _map = new();

    // ── Factory methods ───────────────────────────────────────────

    /// <summary>
    /// Returns a bindings object pre-populated with the default key layout.
    /// Call this before Load() so missing YAML entries fall back gracefully.
    /// </summary>
    public static InputBindings Defaults()
    {
        var b = new InputBindings();

        b.Bind(GameAction.MoveNorth, Keys.W, Keys.Up);
        b.Bind(GameAction.MoveSouth, Keys.S, Keys.Down);
        b.Bind(GameAction.MoveEast, Keys.D, Keys.Right);
        b.Bind(GameAction.MoveWest, Keys.A, Keys.Left);

        b.Bind(GameAction.Interact, Keys.E);
        b.Bind(GameAction.Wait, Keys.Space);

        b.Bind(GameAction.ToggleInventory, Keys.I);

        b.Bind(GameAction.Quit, Keys.Escape);
        b.Bind(GameAction.RegenerateWorld, Keys.R);

        // PathPoint / PathConfirm are mouse actions — no keyboard binding.

        return b;
    }

    /// <summary>
    /// Loads bindings from a YAML file, merging over the defaults.
    /// If the file is missing or unreadable, defaults are used silently.
    /// </summary>
    public static InputBindings Load(string yamlPath)
    {
        var bindings = Defaults();

        if (!File.Exists(yamlPath))
            return bindings;

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var text = File.ReadAllText(yamlPath);
            var raw = deserializer.Deserialize<Dictionary<string, List<string>>>(text);

            if (raw == null) return bindings;

            foreach (var (actionName, keyNames) in raw)
            {
                if (!Enum.TryParse<GameAction>(actionName, ignoreCase: true, out var action))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[InputBindings] Unknown action in controls.yml: '{actionName}'");
                    continue;
                }

                var keys = new List<Keys>();
                foreach (var keyName in keyNames)
                {
                    if (Enum.TryParse<Keys>(keyName, ignoreCase: true, out var key))
                        keys.Add(key);
                    else
                        System.Diagnostics.Debug.WriteLine(
                            $"[InputBindings] Unknown key '{keyName}' for action '{actionName}'");
                }

                if (keys.Count > 0)
                    bindings._map[action] = keys;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[InputBindings] Failed to load '{yamlPath}': {ex.Message}. Using defaults.");
        }

        return bindings;
    }

    // ── Binding API ───────────────────────────────────────────────

    /// <summary>
    /// Set (or replace) the keys bound to an action.
    /// Pass multiple keys to allow alternates (e.g. WASD + arrows).
    /// </summary>
    public void Bind(GameAction action, params Keys[] keys)
    {
        _map[action] = new List<Keys>(keys);
    }

    /// <summary>
    /// Returns all keys currently bound to an action (may be empty).
    /// </summary>
    public IReadOnlyList<Keys> GetKeys(GameAction action)
    {
        return _map.TryGetValue(action, out var list) ? list : Array.Empty<Keys>();
    }

    // ── Query API (used by InputHandler each frame) ───────────────

    /// <summary>
    /// True if any bound key for this action was just freshly pressed
    /// (down this frame, up last frame).
    /// </summary>
    public bool IsNewPress(GameAction action, KeyboardState current, KeyboardState previous)
    {
        if (!_map.TryGetValue(action, out var keys)) return false;
        return keys.Any(k => current.IsKeyDown(k) && previous.IsKeyUp(k));
    }

    /// <summary>
    /// True if any bound key for this action is currently held down.
    /// </summary>
    public bool IsHeld(GameAction action, KeyboardState current)
    {
        if (!_map.TryGetValue(action, out var keys)) return false;
        return keys.Any(k => current.IsKeyDown(k));
    }

    // ── Display helpers (for HUD key hints) ──────────────────────

    /// <summary>
    /// Returns a short human-readable label for the first bound key,
    /// e.g. "W" for MoveNorth, "Space" for Wait.
    /// Returns "?" if unbound.
    /// </summary>
    public string PrimaryKeyLabel(GameAction action)
    {
        var keys = GetKeys(action);
        if (keys.Count == 0) return "?";

        // Make common keys more readable
        return keys[0] switch
        {
            Keys.Space => "Space",
            Keys.Escape => "Esc",
            Keys.Up => "↑",
            Keys.Down => "↓",
            Keys.Left => "←",
            Keys.Right => "→",
            var k => k.ToString()
        };
    }

    /// <summary>
    /// Builds a compact hint string for multiple actions.
    /// e.g. HintLine(bindings, (MoveNorth,"Move"), (Interact,"Interact"))
    ///   → "W: Move  E: Interact"
    /// </summary>
    public static string HintLine(InputBindings b, params (GameAction action, string label)[] hints)
    {
        return string.Join("  ", hints.Select(h => $"{b.PrimaryKeyLabel(h.action)}: {h.label}"));
    }
}