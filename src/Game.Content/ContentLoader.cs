// src/Game.Content/ContentLoader.cs
//
// Discovers content packs, reads YAML files, validates definitions,
// and builds a frozen ContentRegistry.
//
// Boot sequence:
//   1. Discover packs (find pack.yml in each subdirectory)
//   2. Read pack manifest, resolve load order
//   3. For each pack: load tiles, items, monsters, biomes from YAML
//   4. Validate all defs with FluentValidation
//   5. Freeze registry
//
// Currently loads a single pack (BasePack). Multi-pack ordering and
// dependency resolution will be added when mod support is needed.

using System.IO;
using FluentValidation;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Game.Content.Validation;
using Game.Content.Yaml;
using Game.Core.Biomes;
using Game.Core.Items;
using Game.Core.Monsters;
using Game.Core.Tiles;

namespace Game.Content;

public sealed class ContentLoader
{
    private readonly IDeserializer _yaml;
    private readonly TileDefValidator _tileValidator = new();
    private readonly ItemDefValidator _itemValidator = new();
    private readonly BiomeDefValidator _biomeValidator = new();
    private readonly MonsterDefValidator _monsterValidator = new();

    /// <summary>Validation errors accumulated during loading.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Informational messages about what was loaded.</summary>
    public List<string> Log { get; } = new();

    public ContentLoader()
    {
        _yaml = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Load all content from the given root directory.
    /// Expects subdirectories like BasePack/ each containing pack.yml.
    /// Returns a frozen ContentRegistry on success.
    /// </summary>
    /// <param name="contentRoot">Path to the content/ directory.</param>
    public ContentRegistry LoadAll(string contentRoot)
    {
        var registry = new ContentRegistry();
        Errors.Clear();
        Log.Clear();

        if (!Directory.Exists(contentRoot))
        {
            Errors.Add($"Content root directory not found: {contentRoot}");
            return registry;
        }

        // Discover packs (directories containing pack.yml)
        var packDirs = Directory.GetDirectories(contentRoot)
            .Where(d => File.Exists(Path.Combine(d, "pack.yml")))
            .OrderBy(d => d) // BasePack sorts first alphabetically
            .ToList();

        if (packDirs.Count == 0)
        {
            Errors.Add($"No content packs found in: {contentRoot}");
            return registry;
        }

        foreach (var packDir in packDirs)
        {
            LoadPack(packDir, registry);
        }

        if (Errors.Count == 0)
        {
            registry.Freeze();
            Log.Add(registry.ToString());
        }
        else
        {
            Log.Add($"Content loading completed with {Errors.Count} error(s). Registry NOT frozen.");
        }

        return registry;
    }

    /// <summary>Load a single content pack from the given directory.</summary>
    private void LoadPack(string packDir, ContentRegistry registry)
    {
        var packName = Path.GetFileName(packDir);

        // Read manifest
        var manifestPath = Path.Combine(packDir, "pack.yml");
        try
        {
            var manifestText = File.ReadAllText(manifestPath);
            var manifest = _yaml.Deserialize<YamlPackManifest>(manifestText);
            Log.Add($"Loading pack: {manifest.Name} ({manifest.Id}) v{manifest.Version}");
        }
        catch (Exception ex)
        {
            Errors.Add($"[{packName}] Failed to read pack.yml: {ex.Message}");
            return; // skip this pack entirely
        }

        // Load order matters: tiles first (biomes reference them), then items, monsters, biomes
        LoadTiles(packDir, packName, registry);
        LoadItems(packDir, packName, registry);
        LoadMonsters(packDir, packName, registry);
        LoadBiomes(packDir, packName, registry);
    }

    // ── Tile loading ─────────────────────────────────────────────────

    private void LoadTiles(string packDir, string packName, ContentRegistry registry)
    {
        var tilesDir = Path.Combine(packDir, "tiles");
        if (!Directory.Exists(tilesDir)) return;

        foreach (var file in Directory.GetFiles(tilesDir, "*.yml"))
        {
            try
            {
                var text = File.ReadAllText(file);
                var yamlFile = _yaml.Deserialize<YamlTileDefsFile>(text);

                foreach (var yt in yamlFile.Tiles)
                {
                    var tileDef = new TileDef
                    {
                        Id = yt.Id,
                        Name = yt.Name,
                        Walkable = yt.Walkable,
                        Color = yt.Color,
                        Sprite = yt.Sprite,
                        BlocksSight = yt.BlocksSight,
                        Height = yt.Height,
                        Tags = yt.Tags
                    };

                    var result = _tileValidator.Validate(tileDef);
                    if (!result.IsValid)
                    {
                        foreach (var err in result.Errors)
                            Errors.Add($"[{packName}] Tile '{yt.Id}': {err.ErrorMessage}");
                        continue;
                    }

                    registry.RegisterTile(tileDef);
                }

                Log.Add($"  Loaded {yamlFile.Tiles.Count} tile(s) from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Errors.Add($"[{packName}] Error loading {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    // ── Item loading ─────────────────────────────────────────────────

    private void LoadItems(string packDir, string packName, ContentRegistry registry)
    {
        var itemsDir = Path.Combine(packDir, "items");
        if (!Directory.Exists(itemsDir)) return;

        foreach (var file in Directory.GetFiles(itemsDir, "*.yml"))
        {
            try
            {
                var text = File.ReadAllText(file);
                var yamlFile = _yaml.Deserialize<YamlItemDefsFile>(text);

                foreach (var yi in yamlFile.Items)
                {
                    var itemDef = new ItemDef
                    {
                        Id = yi.Id,
                        Name = yi.Name,
                        Tags = yi.Tags,
                        Stackable = yi.Stackable,
                        MaxStack = yi.MaxStack,
                        EffectType = yi.EffectType,
                        EffectAmount = yi.EffectAmount,
                        Color = yi.Color
                    };

                    var result = _itemValidator.Validate(itemDef);
                    if (!result.IsValid)
                    {
                        foreach (var err in result.Errors)
                            Errors.Add($"[{packName}] Item '{yi.Id}': {err.ErrorMessage}");
                        continue;
                    }

                    registry.RegisterItem(itemDef);
                }

                Log.Add($"  Loaded {yamlFile.Items.Count} item(s) from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Errors.Add($"[{packName}] Error loading {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    // ── Monster loading ──────────────────────────────────────────────

    private void LoadMonsters(string packDir, string packName, ContentRegistry registry)
    {
        var monstersDir = Path.Combine(packDir, "monsters");
        if (!Directory.Exists(monstersDir)) return;

        foreach (var file in Directory.GetFiles(monstersDir, "*.yml"))
        {
            try
            {
                var text = File.ReadAllText(file);
                var yamlFile = _yaml.Deserialize<YamlMonsterDefsFile>(text);

                foreach (var ym in yamlFile.Monsters)
                {
                    var monsterDef = new MonsterDef
                    {
                        Id = ym.Id,
                        Name = ym.Name,
                        Tags = ym.Tags,
                        MaxHp = ym.MaxHp,
                        Attack = ym.Attack,
                        Defense = ym.Defense,
                        ThreatCost = ym.ThreatCost,
                        AiBehavior = ym.AiBehavior,
                        SightRange = ym.SightRange,
                        Color = ym.Color,
                        Glyph = ym.Glyph
                    };

                    var result = _monsterValidator.Validate(monsterDef);
                    if (!result.IsValid)
                    {
                        foreach (var err in result.Errors)
                            Errors.Add($"[{packName}] Monster '{ym.Id}': {err.ErrorMessage}");
                        continue;
                    }

                    registry.RegisterMonster(monsterDef);
                }

                Log.Add($"  Loaded {yamlFile.Monsters.Count} monster(s) from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Errors.Add($"[{packName}] Error loading {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    // ── Biome loading ────────────────────────────────────────────────

    private void LoadBiomes(string packDir, string packName, ContentRegistry registry)
    {
        var biomesDir = Path.Combine(packDir, "biomes");
        if (!Directory.Exists(biomesDir)) return;

        var allBiomes = new List<BiomeDef>();

        foreach (var file in Directory.GetFiles(biomesDir, "*.yml"))
        {
            try
            {
                var text = File.ReadAllText(file);
                var yamlFile = _yaml.Deserialize<YamlBiomeDefsFile>(text);

                foreach (var yb in yamlFile.Biomes)
                {
                    var biomeDef = new BiomeDef
                    {
                        Id = yb.Id,
                        Name = yb.Name,
                        TileId = yb.TileId,
                        ElevationMax = yb.ElevationMax,
                        Walkable = yb.Walkable,
                        Tags = yb.Tags
                    };

                    var result = _biomeValidator.Validate(biomeDef);
                    if (!result.IsValid)
                    {
                        foreach (var err in result.Errors)
                            Errors.Add($"[{packName}] Biome '{yb.Id}': {err.ErrorMessage}");
                        continue;
                    }

                    allBiomes.Add(biomeDef);
                }

                Log.Add($"  Loaded {yamlFile.Biomes.Count} biome(s) from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Errors.Add($"[{packName}] Error loading {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (allBiomes.Count > 0)
        {
            // Cross-reference validation: every biome's TileId must exist in the registry
            foreach (var biome in allBiomes)
            {
                if (!registry.Tiles.ContainsKey(biome.TileId))
                {
                    Errors.Add($"[{packName}] Biome '{biome.Id}' references unknown tileId '{biome.TileId}' — " +
                               "make sure tiles are loaded before biomes.");
                }
            }

            registry.RegisterBiomes(allBiomes);
        }
    }
}