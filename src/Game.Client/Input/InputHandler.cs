// src/Game.Client/Input/InputHandler.cs
//
// Translates keyboard input into game actions.
// Turn-based: each key press triggers exactly one action.
//
// Keys are no longer hardcoded here — all bindings come from
// InputBindings, which is loaded from controls.yml at startup.
// InputHandler receives the bindings via constructor injection.

#nullable enable

using Microsoft.Xna.Framework.Input;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Items;

namespace Game.Client.Input;

public class InputHandler
{
    private KeyboardState _previousState;
    private KeyboardState _currentState;

    private readonly InputBindings _bindings;

    /// <summary>
    /// Fired when the player interacts with a transition tile (dungeon entrance/exit).
    /// Game1 subscribes to this and performs the actual map swap.
    /// </summary>
    public event System.Action<TransitionRequest>? OnMapTransition;

    /// <summary>Describes which direction a map transition should go.</summary>
    public enum TransitionRequest
    {
        EnterDungeon,
        ExitDungeon
    }

    /// <summary>
    /// The result of processing input for one frame.
    /// Only one action can happen per frame in a turn-based game.
    /// </summary>
    public enum Action
    {
        None,
        MoveNorth,
        MoveSouth,
        MoveEast,
        MoveWest,
        MoveNorthEast,
        MoveNorthWest,
        MoveSouthEast,
        MoveSouthWest,
        Interact,
        Wait
    }

    public InputHandler(InputBindings bindings)
    {
        _bindings = bindings;
    }

    /// <summary>
    /// Call once per frame in Update(). Captures keyboard state
    /// and returns the action the player wants to take.
    /// </summary>
    public Action GetAction()
    {
        _previousState = _currentState;
        _currentState = Keyboard.GetState();

        if (_bindings.IsNewPress(GameAction.MoveNorth, _currentState, _previousState))
            return Action.MoveNorth;
        if (_bindings.IsNewPress(GameAction.MoveSouth, _currentState, _previousState))
            return Action.MoveSouth;
        if (_bindings.IsNewPress(GameAction.MoveEast, _currentState, _previousState))
            return Action.MoveEast;
        if (_bindings.IsNewPress(GameAction.MoveWest, _currentState, _previousState))
            return Action.MoveWest;
        if (_bindings.IsNewPress(GameAction.MoveNorthEast, _currentState, _previousState))
            return Action.MoveNorthEast;
        if (_bindings.IsNewPress(GameAction.MoveNorthWest, _currentState, _previousState))
            return Action.MoveNorthWest;
        if (_bindings.IsNewPress(GameAction.MoveSouthEast, _currentState, _previousState))
            return Action.MoveSouthEast;
        if (_bindings.IsNewPress(GameAction.MoveSouthWest, _currentState, _previousState))
            return Action.MoveSouthWest;
        if (_bindings.IsNewPress(GameAction.Interact, _currentState, _previousState))
            return Action.Interact;
        if (_bindings.IsNewPress(GameAction.Wait, _currentState, _previousState))
            return Action.Wait;

        return Action.None;
    }

    /// <summary>
    /// Convenience: check if a specific GameAction was freshly pressed this frame.
    /// Only valid after GetAction() has been called (state is updated there).
    /// Used by Game1 for UI toggles (I, R, Escape) that aren't turn actions.
    /// </summary>
    public bool IsNewPress(GameAction action)
        => _bindings.IsNewPress(action, _currentState, _previousState);

    /// <summary>
    /// Process the action against the game state.
    /// Returns true if the action consumed a turn (so enemies should act).
    /// </summary>
    public bool ProcessAction(Action action, GameState state)
    {
        if (action == Action.None) return false;
        if (state.IsGameOver) return false;

        return action switch
        {
            Action.MoveNorth => TryMove(state, 0, -1),
            Action.MoveSouth => TryMove(state, 0, 1),
            Action.MoveEast => TryMove(state, 1, 0),
            Action.MoveWest => TryMove(state, -1, 0),
            Action.MoveNorthEast => TryMove(state, 1, -1),
            Action.MoveNorthWest => TryMove(state, -1, -1),
            Action.MoveSouthEast => TryMove(state, 1, 1),
            Action.MoveSouthWest => TryMove(state, -1, 1),
            Action.Wait => Wait(state),
            Action.Interact => TryInteract(state),
            _ => false,
        };
    }

    // ── Turn actions ──────────────────────────────────────────────

    private static bool Wait(GameState state)
    {
        state.Log("You wait...");
        return true;
    }

    /// <summary>
    /// Attempt to move the player by (dx, dy).
    /// Checks tile walkability and entity collisions.
    /// If an enemy is in the way, bump-attack instead of moving.
    /// </summary>
    private bool TryMove(GameState state, int dx, int dy)
    {
        var player = state.Player;
        int newX = player.X + dx;
        int newY = player.Y + dy;

        if (state.ActiveMap == null || !state.ActiveMap.IsWalkable(newX, newY))
            return false;

        var blocker = state.GetBlockingEntityAt(newX, newY);
        if (blocker != null && blocker != player)
        {
            if (blocker is Enemy enemy)
                return AttackEnemy(state, player, enemy);

            state.Log($"Something blocks your path at ({newX}, {newY}).");
            return true;
        }

        state.MoveEntity(player, dx, dy);
        TryPickupItems(state);
        return true;
    }

    /// <summary>
    /// Resolve a player attack against an enemy.
    /// </summary>
    private static bool AttackEnemy(GameState state, Player player, Enemy enemy)
    {
        int damage = enemy.TakeDamage(player.Attack);

        if (enemy.IsDead)
        {
            state.Log($"You hit {enemy.Name} for {damage} damage — killed!");
            enemy.IsAlive = false;
        }
        else
        {
            state.Log($"You hit {enemy.Name} for {damage} damage. ({enemy.Hp}/{enemy.MaxHp} HP)");
            if (!enemy.IsProvoked)
                enemy.IsProvoked = true;
        }

        return true;
    }

    /// <summary>
    /// Pick up all WorldItem entities at the player's current position.
    /// </summary>
    private static void TryPickupItems(GameState state)
    {
        var player = state.Player;

        for (int i = state.Entities.Count - 1; i >= 0; i--)
        {
            if (state.Entities[i] is WorldItem item
                && item.IsAlive
                && item.X == player.X
                && item.Y == player.Y)
            {
                player.Inventory.Add(item.ItemDef, item.Count);
                item.IsAlive = false;

                string display = item.Count > 1
                    ? $"{item.ItemDef.Name} x{item.Count}"
                    : item.ItemDef.Name;
                state.Log($"Picked up {display}.");
            }
        }
    }

    /// <summary>
    /// Attempt to interact with whatever is at or adjacent to the player.
    /// </summary>
    private bool TryInteract(GameState state)
    {
        var tileId = state.ActiveMap?.GetTileId(state.Player.X, state.Player.Y);

        if (tileId == "base:dungeon_entrance" && state.Mode == GameMode.Overworld)
        {
            OnMapTransition?.Invoke(TransitionRequest.EnterDungeon);
            return true;
        }

        if (tileId == "base:dungeon_exit" && state.Mode == GameMode.Dungeon)
        {
            OnMapTransition?.Invoke(TransitionRequest.ExitDungeon);
            return true;
        }

        if (tileId == "base:dungeon_entrance" && state.Mode == GameMode.Dungeon)
        {
            state.Log("This is where you entered. Find the exit deeper in the dungeon.");
            return false;
        }

        if (TryInteractWithAdjacent(state))
            return true;

        state.Log("Nothing to interact with here.");
        return false;
    }

    private static bool TryInteractWithAdjacent(GameState state)
    {
        int px = state.Player.X;
        int py = state.Player.Y;

        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { -1, 1, 0, 0 };

        for (int d = 0; d < 4; d++)
        {
            int tx = px + dx[d];
            int ty = py + dy[d];

            foreach (var entity in state.Entities)
            {
                if (!entity.IsAlive || entity.X != tx || entity.Y != ty)
                    continue;

                if (entity is Chest chest)
                    return TryOpenChest(chest, state);
            }
        }

        return false;
    }

    private static bool TryOpenChest(Chest chest, GameState state)
    {
        var loot = chest.Open();
        if (loot == null)
        {
            state.Log("This chest is already empty.");
            return false;
        }

        var (def, count) = loot.Value;
        state.Player.Inventory.Add(def, count);

        string display = count > 1 ? $"{def.Name} x{count}" : def.Name;
        state.Log($"Opened chest: found {display}!");

        return true;
    }
}