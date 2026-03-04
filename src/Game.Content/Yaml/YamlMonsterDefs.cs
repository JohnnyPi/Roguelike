// src/Game.Content/Yaml/YamlMonsterDefs.cs
//
// YamlDotNet deserialization models for monsters/*.yml files.

using YamlDotNet.Serialization;

namespace Game.Content.Yaml;

public sealed class YamlMonsterDefsFile
{
    [YamlMember(Alias = "schema")]
    public string Schema { get; set; } = string.Empty;

    [YamlMember(Alias = "monsters")]
    public List<YamlMonsterDef> Monsters { get; set; } = new();
}

public sealed class YamlMonsterDef
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();

    [YamlMember(Alias = "maxHp")]
    public int MaxHp { get; set; } = 1;

    [YamlMember(Alias = "attack")]
    public int Attack { get; set; } = 1;

    [YamlMember(Alias = "defense")]
    public int Defense { get; set; } = 0;

    [YamlMember(Alias = "threatCost")]
    public int ThreatCost { get; set; } = 1;

    [YamlMember(Alias = "aiBehavior")]
    public string AiBehavior { get; set; } = "chase";

    [YamlMember(Alias = "sightRange")]
    public int SightRange { get; set; } = 8;

    [YamlMember(Alias = "color")]
    public string Color { get; set; } = "#FF0000";

    [YamlMember(Alias = "glyph")]
    public string Glyph { get; set; } = "?";
}