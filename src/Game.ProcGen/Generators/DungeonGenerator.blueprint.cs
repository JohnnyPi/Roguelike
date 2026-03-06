// src/Game.ProcGen/Generators/DungeonGenerator.Blueprint.cs

// Blueprint-driven generation entry point.
//
// This file is intentionally empty for now. It exists to establish the split
// boundary before blueprint work begins, so that code lands here and never
// back-fills into DungeonGenerator.cs.
//
// When blueprint generation is implemented, add a method alongside Generate():
//
//   public TileMap GenerateFromBlueprint(
//       DungeonBlueprint blueprint,
//       Dictionary<string, TileDef> tileRegistry,
//       int? seed = null)
//   { ... }
//
// The blueprint pipeline (see Dungeon_Blueprint_schema) maps pipeline.steps.kind
// to IGenStep implementations. Steps are resolved and executed in order,
// replacing or augmenting the hardcoded rooms-and-corridors algorithm above.
//
// Shared infrastructure (tile IDs, output properties, Room struct) lives in
// DungeonGenerator.cs and is available to this file via partial class.

namespace Game.ProcGen.Generators;

public partial class DungeonGenerator
{
}