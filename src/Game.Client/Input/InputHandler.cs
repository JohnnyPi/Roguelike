// src/Game.Client/Input/InputHandler.cs

#nullable enable

using Microsoft.Xna.Framework.Input;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Items;

namespace Game.Client.Input;

/// <summary>
/// Translates keyboard input into game actions.
/// Turn-based: each key press triggers exactly one action.
/// Uses previous/current state comparison to detect fresh presses,
/// preventing held keys from firing every frame.
/// </summary>
public class InputHandler
{
    private KeyboardState _previousState;
    private KeyboardState _currentState;

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
        Interact,
        Wait
    }

    /// <summary>
    /// Call once per frame in Update(). Captures keyboard state
    /// and returns the action the player wants to take.
    /// </summary>
    public Action GetAction()
    {
        _previousState = _currentState;
        _currentState = Keyboard.GetState();

        // Movement � WASD and arrow keys
        if (IsNewPress(Keys.W) || IsNewPress(Keys.Up))
            return Action.MoveNorth;
        if (IsNewPress(Keys.S) || IsNewPress(Keys.Down))
            return Action.MoveSouth;
        if (IsNewPress(Keys.D) || IsNewPress(Keys.Right))
            return Action.MoveEast;
        if (IsNewPress(Keys.A) || IsNewPress(Keys.Left))
            return Action.MoveWest;

        // Interact (open chests, enter dungeons, etc.)
        if (IsNewPress(Keys.E))
            return Action.Interact;

        // Wait / skip turn
        if (IsNewPress(Keys.Space))
            return Action.Wait;

        return Action.None;
    }

    /// <summary>
    /// Process the action against the game state.
    /// Returns true if the action consumed a turn (so enemies should act).
    /// </summary>
    public bool ProcessAction(Action action, GameState state)
    {
        if (action == Action.None) return false;
        if (state.IsGameOver) return false;

        switch (action)
        {
            case Action.MoveNorth: return TryMove(state, 0, -1);
            case Action.MoveSouth: return TryMove(state, 0, 1);
            case Action.MoveEast: return TryMove(state, 1, 0);
            case Action.MoveWest: return TryMove(state, -1, 0);
            case Action.Wait:
                state.Log("You wait...");
                return true; // waiting still consumes a turn
            case Action.Interact:
                return TryInteract(state);
            default:
                return false;
        }
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

        // Check map bounds and tile walkability
        if (state.ActiveMap == null || !state.ActiveMap.IsWalkable(newX, newY))
            return false;

        // Check for blocking entity (this is how bump-attack works)
        var blocker = state.GetBlockingEntityAt(newX, newY);
        if (blocker != null && blocker != player)
        {
            // Phase 6 will add combat here.
            // For now, just log and consume the turn.
            state.Log($"Something blocks your path at ({newX}, {newY}).");
            return true;
        }

        // Clear to move
        player.Move(dx, dy);

        // Auto-pickup: collect any items at the new position
        TryPickupItems(state);

        return true;
    }

    /// <summary>
    /// Pick up all WorldItem entities at the player's current position.
    /// Items are added to inventory and removed from the world.
    /// </summary>
    private void TryPickupItems(GameState state)
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
    /// Attempt to interact with whatever is at the player's position or adjacent.
    /// Transition tiles: must be standing on them.
    /// Chests: must be adjacent (1 tile away in cardinal direction).
    /// Phase 5 adds chest opening.
    /// </summary>
    private bool TryInteract(GameState state)
    {
        // Check if standing on a special tile
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

        // Standing on dungeon_entrance while IN the dungeon — this is the entry point, 
        // not an exit. Let the player know.
        if (tileId == "base:dungeon_entrance" && state.Mode == GameMode.Dungeon)
        {
            state.Log("This is where you entered. Find the exit deeper in the dungeon.");
            return false;
        }

        // Check adjacent tiles for interactable entities (chests)
        if (TryInteractWithAdjacent(state))
            return true;

        state.Log("Nothing to interact with here.");
        return false;
    }

    /// <summary>
    /// Check the four cardinal neighbors for an interactable entity.
    /// Currently handles chests; more types can be added later.
    /// </summary>
    private bool TryInteractWithAdjacent(GameState state)
    {
        int px = state.Player.X;
        int py = state.Player.Y;

        // Check all 4 cardinal directions
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
                {
                    return TryOpenChest(chest, state);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Open a chest, adding its loot to the player's inventory.
    /// </summary>
    private bool TryOpenChest(Chest chest, GameState state)
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

        return true; // consumes a turn
    }

    /// <summary>
    /// True only on the frame the key transitions from up to down.
    /// This is what makes movement turn-based instead of continuous.
    /// </summary>
    private bool IsNewPress(Keys key)
    {
        return _currentState.IsKeyDown(key) && _previousState.IsKeyUp(key);
    }

    /// <summary>
    /// Public version of IsNewPress for use by Game1 (e.g., R to regenerate).
    /// Note: only valid after GetAction() has been called this frame,
    /// since that's where keyboard state gets updated.
    /// </summary>
    public bool IsNewKeyPress(Keys key)
    {
        return _currentState.IsKeyDown(key) && _previousState.IsKeyUp(key);
    }
}