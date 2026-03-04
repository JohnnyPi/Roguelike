// src/Game.Content/Yaml/YamlTileDefs.cs

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

    /// <summary>Blocks FOW line-of-sight. Default false = transparent.</summary>
    [YamlMember(Alias = "blocksSight")]
    public bool BlocksSight { get; set; }

    /// <summary>Logical height 0-4. Default 1 = flat ground.</summary>
    [YamlMember(Alias = "height")]
    public int Height { get; set; } = 1;

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();
}