// src/Game.Content/Yaml/YamlTileDefs.cs
//
// YamlDotNet deserialization models for tiles/*.yml files.
// These are simple POCOs that map 1:1 to the YAML structure.
// The loader converts them into Game.Core.Tiles.TileDef instances.

using YamlDotNet.Serialization;

namespace Game.Content.Yaml;

public sealed class YamlTileDefsFile
{
    [YamlMember(Alias = "schema")]
    public string Schema { get; set; } = string.Empty;

    [YamlMember(Alias = "tiles")]
    public List<YamlTileDef> Tiles { get; set; } = new();
}

public sealed class YamlTileDef
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "walkable")]
    public bool Walkable { get; set; }

    [YamlMember(Alias = "color")]
    public string Color { get; set; } = "#FF00FF";

    [YamlMember(Alias = "sprite")]
    public string Sprite { get; set; } = string.Empty;

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();
}