// src/Game.Content/Yaml/YamlPackManifest.cs
//
// YamlDotNet deserialization model for pack.yml.

using YamlDotNet.Serialization;

namespace Game.Content.Yaml;

public sealed class YamlPackManifest
{
    [YamlMember(Alias = "schema")]
    public string Schema { get; set; } = string.Empty;

    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "0.1.0";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;

    [YamlMember(Alias = "author")]
    public string Author { get; set; } = string.Empty;

    [YamlMember(Alias = "dependencies")]
    public List<string> Dependencies { get; set; } = new();
}