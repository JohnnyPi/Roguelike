// src/Game.Content/Yaml/YamlBiomeDefs.cs
//
// YamlDotNet deserialization models for biomes/*.yml files.
// moistureMin/Max and temperatureMin/Max are optional -- omitting defaults to 0/1.

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

    [YamlMember(Alias = "moistureMin")]
    public float MoistureMin { get; set; } = 0.0f;

    [YamlMember(Alias = "moistureMax")]
    public float MoistureMax { get; set; } = 1.0f;

    [YamlMember(Alias = "temperatureMin")]
    public float TemperatureMin { get; set; } = 0.0f;

    [YamlMember(Alias = "temperatureMax")]
    public float TemperatureMax { get; set; } = 1.0f;

    [YamlMember(Alias = "walkable")]
    public bool Walkable { get; set; } = true;

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();
}