// src/Game.Client/Input/InputHandler.cs

using Microsoft.Xna.Framework.Input;
using Game.Core;
using Game.Core.Entities;

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

        // Movement — WASD and arrow keys
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
        return true;
    }

    /// <summary>
    /// Attempt to interact with whatever is at the player's position.
    /// Phase 4 adds dungeon entry, Phase 5 adds chest opening.
    /// </summary>
    private bool TryInteract(GameState state)
    {
        // Check if standing on a special tile
        var tileId = state.ActiveMap?.GetTileId(state.Player.X, state.Player.Y);

        if (tileId == "base:dungeon_entrance")
        {
            // Phase 4 will handle map transitions here.
            state.Log("You see a dungeon entrance. (Transition not yet implemented.)");
            return false; // don't consume turn for unimplemented interaction
        }

        state.Log("Nothing to interact with here.");
        return false;
    }

    /// <summary>
    /// True only on the frame the key transitions from up to down.
    /// This is what makes movement turn-based instead of continuous.
    /// </summary>
    private bool IsNewPress(Keys key)
    {
        return _currentState.IsKeyDown(key) && _previousState.IsKeyUp(key);
    }
}