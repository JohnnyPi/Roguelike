// src/Game.Client/Input/GameAction.cs
//
// Canonical enumeration of every player-triggerable action.
// InputBindings maps these to physical keys/buttons.
// InputHandler and Game1 reference only GameAction — never raw Keys.
//
// Adding a new action:
//   1. Add the value here.
//   2. Add a default binding in InputBindings.Defaults().
//   3. Handle it in InputHandler.ProcessAction() or Game1.Update().

namespace Game.Client.Input;

public enum GameAction
{
    // ── Movement ──────────────────────────────────────────────────
    MoveNorth,
    MoveSouth,
    MoveEast,
    MoveWest,
    MoveNorthEast,
    MoveNorthWest,
    MoveSouthEast,
    MoveSouthWest,

    // ── In-world actions ──────────────────────────────────────────
    Interact,   // open chests, enter/exit dungeons
    Wait,       // skip turn

    // ── Mouse / path actions (handled separately in PathController)
    PathPoint,  // right-click: set path destination
    PathConfirm,// right-click same tile: confirm and begin movement

    // ── UI toggles ────────────────────────────────────────────────
    ToggleInventory,

    // ── Meta / debug ─────────────────────────────────────────────
    Quit,
    RegenerateWorld, // debug: R key
}