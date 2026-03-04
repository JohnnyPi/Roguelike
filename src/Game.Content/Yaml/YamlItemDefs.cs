// src/Game.Content/Yaml/YamlItemDefs.cs
//
// YamlDotNet deserialization models for items/*.yml files.

using YamlDotNet.Serialization;

namespace Game.Content.Yaml;

public sealed class YamlItemDefsFile
{
    [YamlMember(Alias = "schema")]
    public string Schema { get; set; } = string.Empty;

    [YamlMember(Alias = "items")]
    public List<YamlItemDef> Items { get; set; } = new();
}

public sealed class YamlItemDef
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "tags")]
    public List<string> Tags { get; set; } = new();

    [YamlMember(Alias = "stackable")]
    public bool Stackable { get; set; }

    [YamlMember(Alias = "maxStack")]
    public int MaxStack { get; set; } = 1;

    [YamlMember(Alias = "effectType")]
    public string EffectType { get; set; } = string.Empty;

    [YamlMember(Alias = "effectAmount")]
    public int EffectAmount { get; set; }

    [YamlMember(Alias = "color")]
    public string Color { get; set; } = "#FFFF00";
}