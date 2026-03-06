// src/Game.Client/Game1.WorldTransitions.cs

using Game.Client.Input;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Lighting;
using Game.Core.Tiles;
using Game.ProcGen.Generators;
using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace Game.Client;

public partial class Game1
{
    // -- Map Transition Handler -------------------------------------------

    private void HandleMapTransition(InputHandler.TransitionRequest request)
    {
        switch (request)
        {
            case InputHandler.TransitionRequest.EnterDungeon:
                EnterDungeon();
                break;
            case InputHandler.TransitionRequest.ExitDungeon:
                ExitToOverworld();
                break;
        }
    }

    /// <summary>
    /// Transition from overworld into a freshly generated dungeon.
    /// Saves overworld state so we can restore it on exit.
    /// </summary>
    private void EnterDungeon()
    {
        // Save overworld state for return trip
        _state.OverworldMap = _state.ActiveMap;
        _state.OverworldPlayerPosition = (_state.Player.X, _state.Player.Y);

        _dungeonSeed = Environment.TickCount;

        var generator = new DungeonGenerator
        {
            MapWidth = 60,
            MapHeight = 40,
            MinRooms = 6,
            MaxRooms = 12,
            RoomMinSize = 5,
            RoomMaxSize = 10,
            // Use the cave lighting preset -- will eventually come from blueprint YAML
            LightingConfig = DungeonLightingConfig.Cave
        };

        var tileDict = new Dictionary<string, TileDef>(_content.Tiles);
        var monsterList = _content.MonsterList.Count > 0 ? _content.MonsterList.ToList() : null;
        var map = generator.Generate(tileDict, _dungeonSeed, _content.ItemList.ToList(), monsterList);

        // -- Initialize dungeon lighting ---------------------------------
        var lightingCfg = generator.LightingConfig;
        map.InitializeLighting(lightingCfg.AmbientLight);

        // Copy generated torch positions into state
        _state.LightSources = new List<LightSource>(generator.LightSources);

        // Set FOV radius from blueprint config
        _state.BaseFovRadius = lightingCfg.PlayerFovRadius;

        // Initial lighting pass (before flicker kicks in)
        map.Lighting!.Recompute(_state.LightSources);

        // -- Swap active map ---------------------------------------------
        _state.ActiveMap = map;
        _state.Mode = GameMode.Dungeon;
        _state.Player.SetPosition(generator.EntrancePosition.X, generator.EntrancePosition.Y);

        // Initial FOV from entrance position
        map.Visibility?.Recompute(
            generator.EntrancePosition.X,
            generator.EntrancePosition.Y,
            _state.EffectiveFovRadius
        );

        // -- Populate entities -------------------------------------------
        _state.Entities.Clear();
        foreach (var entity in generator.SpawnedEntities)
            _state.Entities.Add(entity);

        int enemyCount = generator.SpawnedEntities.OfType<Enemy>().Count();

        _state.Log("------------------------------");
        _state.Log($"You descend into the dungeon... (seed: {_dungeonSeed}, {generator.Rooms.Count} rooms, {enemyCount} enemies)");
        _state.Log("Find the green exit tile to return to the overworld.");
        _state.Log($"{_bindings.PrimaryKeyLabel(GameAction.MoveNorth)}{_bindings.PrimaryKeyLabel(GameAction.MoveSouth)}{_bindings.PrimaryKeyLabel(GameAction.MoveEast)}{_bindings.PrimaryKeyLabel(GameAction.MoveWest)} to move. Walk into enemies to attack. {_bindings.PrimaryKeyLabel(GameAction.Interact)} to interact.");
    }

    /// <summary>
    /// Transition from dungeon back to the overworld.
    /// Restores the saved overworld map and player position.
    /// </summary>
    private void ExitToOverworld()
    {
        if (_state.OverworldMap == null || _state.OverworldPlayerPosition == null)
        {
            _state.Log("ERROR: No overworld to return to!");
            return;
        }

        // Restore the overworld map and mode
        _state.ActiveMap = _state.OverworldMap;
        _state.Mode = GameMode.Overworld;

        // Restore player position
        var pos = _state.OverworldPlayerPosition.Value;
        _state.Player.SetPosition(pos.X, pos.Y);

        // Clear dungeon entities and light sources
        _state.Entities.Clear();
        _state.LightSources.Clear();

        // Restore overworld FOV radius
        _state.BaseFovRadius = 12;

        // Recompute FOV at restored position
        _state.ActiveMap?.Visibility?.Recompute(
            pos.X,
            pos.Y,
            _state.EffectiveFovRadius
        );

        // Re-sync overworld lighting ambient to current clock/weather state
        if (_state.ActiveMap?.Lighting != null)
        {
            _state.ActiveMap.Lighting.AmbientLight = _state.OverworldAmbient;
            _state.ActiveMap.Lighting.Recompute(_state.LightSources);
        }

        _state.Log("------------------------------");
        _state.Log("You emerge from the dungeon, back on the overworld.");
        _state.Log($"{_bindings.PrimaryKeyLabel(GameAction.MoveNorth)}{_bindings.PrimaryKeyLabel(GameAction.MoveSouth)}{_bindings.PrimaryKeyLabel(GameAction.MoveEast)}{_bindings.PrimaryKeyLabel(GameAction.MoveWest)} to move. {_bindings.PrimaryKeyLabel(GameAction.Interact)} on the red entrance to re-enter.");
    }
}