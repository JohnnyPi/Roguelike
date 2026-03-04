// src/Game.Client/Input/PathController.cs
//
// Manages right-click pathfinding as a three-state machine:
//
//   Idle ──(right-click walkable tile)──► Preview
//     ▲                                      │
//     │   (keyboard move / Escape)           │ (right-click same tile again)
//     │                                      ▼
//     └────────────(arrived / interrupted)── Moving
//
// State: Idle
//   Nothing shown. Mouse hover has no path effect.
//
// State: Preview
//   A path is computed from player → hovered/clicked tile.
//   PreviewPath is populated for TileRenderer to draw.
//   Right-clicking the same destination tile confirms and starts Moving.
//   Right-clicking a different tile re-targets (stays in Preview).
//   Any keyboard action or Escape cancels back to Idle.
//
// State: Moving
//   Each call to Tick() advances the player one step along StoredPath.
//   After each step, living enemies are checked — if any are within
//   SightRadius tiles of the player (Chebyshev distance), movement stops.
//   Arriving at the destination also returns to Idle.
//
// Turn integration:
//   Tick() returns true when it advances the player (a turn was consumed),
//   so Game1 can run enemy AI the same way as keyboard movement.
//
// GoRogue pathfinding note:
//   This uses a simple BFS for now. When GoRogue is wired up properly,
//   swap FindPath() internals for AStar from GoRogue — the public API
//   of PathController does not change.

#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Map;

namespace Game.Client.Input;

public sealed class PathController
{
    // ── Configuration ─────────────────────────────────────────────

    /// <summary>
    /// Chebyshev (8-directional) distance at which a visible enemy
    /// interrupts auto-movement. 8 tiles feels natural for a roguelike.
    /// </summary>
    public int SightRadius { get; set; } = 8;

    // ── State machine ─────────────────────────────────────────────

    public enum PathState { Idle, Preview, Moving }

    public PathState State { get; private set; } = PathState.Idle;

    /// <summary>
    /// The destination tile the player right-clicked.
    /// Valid in Preview and Moving states.
    /// </summary>
    public Point? Destination { get; private set; }

    /// <summary>
    /// The computed path for the current preview/movement.
    /// Index 0 = first step (adjacent to player), last = destination.
    /// TileRenderer reads this to draw the path overlay.
    /// </summary>
    public IReadOnlyList<Point> PreviewPath => _path;

    private readonly List<Point> _path = new();
    private int _stepIndex;

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Feed mouse input into the state machine each frame.
    /// Call this before checking State for the current frame.
    /// Does NOT consume turns — only updates path state.
    /// </summary>
    public void HandleMouse(MouseInputHandler mouse, GameState state)
    {
        if (!mouse.RightClickDown) return;

        var tile = mouse.HoveredTile;
        if (tile == null) return;

        if (state.ActiveMap == null) return;

        switch (State)
        {
            case PathState.Idle:
                TryBeginPreview(tile.Value, state);
                break;

            case PathState.Preview:
                if (tile.Value == Destination)
                    ConfirmPath();                          // second click = go
                else
                    TryBeginPreview(tile.Value, state);    // different tile = re-target
                break;

            case PathState.Moving:
                // Right-click while moving cancels current movement, starts new preview
                Cancel();
                TryBeginPreview(tile.Value, state);
                break;
        }
    }

    /// <summary>
    /// Called by InputHandler/Game1 when any keyboard movement key is pressed.
    /// Cancels path preview or movement so keyboard takes over cleanly.
    /// </summary>
    public void CancelOnKeyboardInput()
    {
        if (State != PathState.Idle)
            Cancel();
    }

    /// <summary>
    /// Advance the player one step along the confirmed path.
    /// Returns true if a step was taken (turn consumed), false if nothing happened.
    /// Call this from Game1.Update() when State == Moving, in place of
    /// normal keyboard action processing.
    /// </summary>
    public bool Tick(GameState state)
    {
        if (State != PathState.Moving) return false;
        if (_stepIndex >= _path.Count)
        {
            // Arrived
            state.Log("Arrived.");
            Cancel();
            return false;
        }

        // Check for enemies in sight before each step
        if (EnemySighted(state))
        {
            state.Log("You stop as you spot an enemy!");
            Cancel();
            return false;
        }

        // Take one step
        var next = _path[_stepIndex];
        var player = state.Player;

        // Sanity check: if the tile became unwalkable (rare), abort
        if (state.ActiveMap != null && !state.ActiveMap.IsWalkable(next.X, next.Y))
        {
            state.Log("Path blocked — stopping.");
            Cancel();
            return false;
        }

        // Check for a blocking entity that appeared mid-path (enemy moved into it, etc.)
        var blocker = state.GetBlockingEntityAt(next.X, next.Y);
        if (blocker != null)
        {
            state.Log("Something is blocking your path.");
            Cancel();
            return false;
        }

        player.Move(next.X - player.X, next.Y - player.Y);
        _stepIndex++;
        state.TryPickupItems();

        // Check enemy sight again after stepping (we may have walked into view range)
        if (EnemySighted(state))
        {
            state.Log("You stop as you spot an enemy!");
            Cancel();
            // Still return true — the step happened, enemies should react
        }

        // Reached end of path
        if (_stepIndex >= _path.Count && State == PathState.Moving)
        {
            state.Log("Arrived.");
            Cancel();
        }

        return true; // turn consumed
    }

    /// <summary>Cancel whatever state we're in and return to Idle.</summary>
    public void Cancel()
    {
        State = PathState.Idle;
        Destination = null;
        _path.Clear();
        _stepIndex = 0;
    }

    // ── Internal helpers ──────────────────────────────────────────

    private void TryBeginPreview(Point target, GameState state)
    {
        if (state.ActiveMap == null) return;

        // Don't path to an unwalkable tile
        if (!state.ActiveMap.IsWalkable(target.X, target.Y))
        {
            state.Log("Can't path there.");
            return;
        }

        var from = new Point(state.Player.X, state.Player.Y);
        var found = FindPath(from, target, state.ActiveMap);

        if (found == null || found.Count == 0)
        {
            state.Log("No path found.");
            return;
        }

        _path.Clear();
        _path.AddRange(found);
        _stepIndex = 0;
        Destination = target;
        State = PathState.Preview;
    }

    private void ConfirmPath()
    {
        if (_path.Count == 0)
        {
            Cancel();
            return;
        }

        _stepIndex = 0;
        State = PathState.Moving;
    }

    /// <summary>
    /// BFS pathfinder on the tile grid.
    /// Returns a list of steps NOT including the starting tile,
    /// ending at (to). Returns null if no path exists.
    ///
    /// TODO: swap internals for GoRogue AStar once it's wired to the map adapter.
    /// The public PathController API does not need to change.
    /// </summary>
    private static List<Point>? FindPath(Point from, Point to, TileMap map)
    {
        if (from == to) return new List<Point>();

        // BFS
        var queue = new Queue<Point>();
        var cameFrom = new Dictionary<Point, Point>();

        queue.Enqueue(from);
        cameFrom[from] = from;

        int[] dx = { 0, 0, 1, -1, 1, -1, 1, -1 };
        int[] dy = { -1, 1, 0, 0, -1, -1, 1, 1 };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current == to)
                return ReconstructPath(cameFrom, from, to);

            for (int d = 0; d < 8; d++)
            {
                var next = new Point(current.X + dx[d], current.Y + dy[d]);

                if (cameFrom.ContainsKey(next)) continue;
                if (!map.IsWalkable(next.X, next.Y)) continue;

                cameFrom[next] = current;
                queue.Enqueue(next);
            }
        }

        return null; // no path
    }

    private static List<Point> ReconstructPath(Dictionary<Point, Point> cameFrom, Point from, Point to)
    {
        var path = new List<Point>();
        var current = to;

        while (current != from)
        {
            path.Add(current);
            current = cameFrom[current];
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Returns true if any living enemy is within SightRadius tiles
    /// of the player (Chebyshev / 8-directional distance).
    /// </summary>
    private bool EnemySighted(GameState state)
    {
        var player = state.Player;

        foreach (var entity in state.Entities)
        {
            if (!entity.IsAlive) continue;
            if (entity is not Enemy) continue;

            int dx = Math.Abs(entity.X - player.X);
            int dy = Math.Abs(entity.Y - player.Y);
            int chebyshev = Math.Max(dx, dy);

            if (chebyshev <= SightRadius)
                return true;
        }

        return false;
    }
}