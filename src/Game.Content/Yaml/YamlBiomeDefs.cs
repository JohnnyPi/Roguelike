// src/Game.Content/Yaml/YamlBiomeDefs.cs
//
// YamlDotNet deserialization models for biomes/*.yml files.
// moistureMin/Max are optional — omitting them defaults to 0/1 (matches all moisture).

using YamlDotNet.Serialization;

namespace Game.Content.Yaml;

public sealed class YamlBiomeDefsFile
{
    [YamlMember(Alias = "schema")]
    public string Schema { get; set; } = string.Empty;

    [YamlMember(Alias = "biomes")]
    public List<YamlBiomeDef> Biomes { get; set; } = new();
}

public sealed class YamlBiomeDef
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "tileId")]
    public string TileId { get; set; } = string.Empty;

    [YamlMember(Alias = "elevationMin")]
    public float ElevationMin { get; set; } = 0.0f;

    [YamlMember(Alias = "elevationMax")]
    public float ElevationMax { get; set; } = 1.0f;

    /// <summary>Optional. Omit to match all moisture values (backward compatible).</summary>
    [YamlMember(Alias = "moistureMin")]
    public float MoistureMin { get; set; } = 0.0f;

    /// <summary>Optional. Omit to match all moisture values (backward compatible).</summary>
    [YamlMember(Alias = "moistureMax")]
    public float MoistureMax { get; set; } = 1.0f;

    [YamlMember(Alias = "walkable")]
    public bool Walkable { get; set; } = true;

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();
}