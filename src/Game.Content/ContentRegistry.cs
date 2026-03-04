// src/Game.Content/ContentRegistry.cs
//
// Central frozen registry holding all loaded content definitions.
// Built by ContentLoader, then passed (read-only) to game systems.
//
// Design:
//   - Each def type gets its own Dictionary<string, T> keyed by Id.
//   - Once Freeze() is called, no further modifications are allowed.
//   - Provides tag-based query helpers for flexible lookups.

using Game.Core.Biomes;
using Game.Core.Items;
using Game.Core.Monsters;
using Game.Core.Tiles;

namespace Game.Content;

public sealed class ContentRegistry
{
    private Dictionary<string, TileDef> _tiles = new();
    private Dictionary<string, ItemDef> _items = new();
    private Dictionary<string, MonsterDef> _monsters = new();
    private List<BiomeDef> _biomes = new(); // kept sorted by ElevationMax
    private bool _frozen;

    // ── Tile access ──────────────────────────────────────────────────

    public IReadOnlyDictionary<string, TileDef> Tiles => _tiles;

    public TileDef GetTile(string id)
    {
        if (_tiles.TryGetValue(id, out var tile))
            return tile;
        throw new KeyNotFoundException($"Tile def not found: '{id}'");
    }

    public bool TryGetTile(string id, out TileDef? tile) => _tiles.TryGetValue(id, out tile);

    // ── Item access ──────────────────────────────────────────────────

    public IReadOnlyDictionary<string, ItemDef> Items => _items;

    /// <summary>All item defs as a list (for systems that iterate them).</summary>
    public IReadOnlyList<ItemDef> ItemList => _items.Values.ToList();

    public ItemDef GetItem(string id)
    {
        if (_items.TryGetValue(id, out var item))
            return item;
        throw new KeyNotFoundException($"Item def not found: '{id}'");
    }

    public bool TryGetItem(string id, out ItemDef? item) => _items.TryGetValue(id, out item);

    /// <summary>Query items by tag. Returns all items that have ALL specified tags.</summary>
    public IEnumerable<ItemDef> QueryItemsByTags(params string[] tags)
    {
        return _items.Values.Where(item =>
            tags.All(tag => item.Tags.Contains(tag)));
    }

    // ── Monster access ───────────────────────────────────────────────

    public IReadOnlyDictionary<string, MonsterDef> Monsters => _monsters;

    /// <summary>All monster defs as a list (for systems that iterate them).</summary>
    public IReadOnlyList<MonsterDef> MonsterList => _monsters.Values.ToList();

    public MonsterDef GetMonster(string id)
    {
        if (_monsters.TryGetValue(id, out var monster))
            return monster;
        throw new KeyNotFoundException($"Monster def not found: '{id}'");
    }

    public bool TryGetMonster(string id, out MonsterDef? monster) => _monsters.TryGetValue(id, out monster);

    /// <summary>Query monsters by tag. Returns all monsters that have ALL specified tags.</summary>
    public IEnumerable<MonsterDef> QueryMonstersByTags(params string[] tags)
    {
        return _monsters.Values.Where(m =>
            tags.All(tag => m.Tags.Contains(tag)));
    }

    // ── Biome access ─────────────────────────────────────────────────

    /// <summary>Biomes sorted by ElevationMax ascending (for threshold lookup).</summary>
    public IReadOnlyList<BiomeDef> Biomes => _biomes;

    // ── Registration (used by ContentLoader before freeze) ───────────

    public void RegisterTile(TileDef tile)
    {
        ThrowIfFrozen();
        _tiles[tile.Id] = tile;
    }

    public void RegisterItem(ItemDef item)
    {
        ThrowIfFrozen();
        _items[item.Id] = item;
    }

    public void RegisterMonster(MonsterDef monster)
    {
        ThrowIfFrozen();
        _monsters[monster.Id] = monster;
    }

    public void RegisterBiomes(IEnumerable<BiomeDef> biomes)
    {
        ThrowIfFrozen();
        _biomes = biomes.OrderBy(b => b.ElevationMax).ToList();
    }

    // ── Freeze ───────────────────────────────────────────────────────

    /// <summary>
    /// Lock the registry. No further registrations allowed.
    /// Call this after all packs have been loaded and validated.
    /// </summary>
    public void Freeze()
    {
        _frozen = true;
    }

    public bool IsFrozen => _frozen;

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("ContentRegistry is frozen — no further registrations allowed.");
    }

    // ── Summary (for logging) ────────────────────────────────────────

    public override string ToString()
    {
        return $"ContentRegistry: {_tiles.Count} tiles, {_items.Count} items, " +
               $"{_monsters.Count} monsters, {_biomes.Count} biomes (frozen={_frozen})";
    }
}