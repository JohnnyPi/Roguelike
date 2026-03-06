// src/Game.Core/Map/ChunkManager.cs

namespace Game.Core.Map;

/// <summary>
/// Manages the resident window of WorldChunks around the player's focus point.
///
/// Resident window: a square of (2*ResidentRadius+1)^2 chunks centered on the
/// focus chunk.  Chunks inside are kept in memory; chunks that fall outside are
/// evicted (dirty ones are handed off to the save callback first).
///
/// Generation is delegated to a Func supplied at construction -- typically
/// OverworldGenerator.GenerateChunk().  This keeps Game.Core free of ProcGen deps.
///
/// Persistence hooks are optional callbacks; wire them up from Game1 or a
/// persistence layer when that phase is implemented.
/// </summary>
public sealed class ChunkManager
{
    // -- Config ----------------------------------------------------------

    public const int DefaultResidentRadius = 3;
    public const int DefaultSimRadius = 1;

    private readonly int _worldWidth;
    private readonly int _worldHeight;
    private readonly int _chunkSize;
    private readonly int _residentRadius;
    private readonly int _simRadius;

    // -- State -----------------------------------------------------------

    private readonly Dictionary<(int, int), WorldChunk> _chunks = new();

    // Current focus in chunk coords
    private int _focusCX;
    private int _focusCY;
    private bool _initialised;

    // -- Delegates -------------------------------------------------------

    /// <summary>
    /// Called when a chunk needs to be generated for the first time or
    /// when no saved data exists.  Signature: (chunkX, chunkY) -> WorldChunk.
    /// </summary>
    public Func<int, int, WorldChunk> GenerateChunk { get; set; } = null!;

    /// <summary>
    /// Optional: called when a dirty chunk is evicted so it can be saved.
    /// If null, dirty data is silently discarded (acceptable during early dev).
    /// </summary>
    public Action<WorldChunk>? SaveChunk { get; set; }

    /// <summary>
    /// Optional: called at load time to try restoring a previously saved chunk.
    /// Return null to fall back to generation.
    /// </summary>
    public Func<int, int, WorldChunk?>? LoadChunk { get; set; }

    // -- Construction ----------------------------------------------------

    public ChunkManager(
        int worldWidth,
        int worldHeight,
        int chunkSize = WorldChunk.Size,
        int residentRadius = DefaultResidentRadius,
        int simRadius = DefaultSimRadius)
    {
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        _chunkSize = chunkSize;
        _residentRadius = residentRadius;
        _simRadius = simRadius;
    }

    // -- Public API ------------------------------------------------------

    /// <summary>
    /// Update the focus world position (call after every player move on the
    /// overworld).  Loads newly required chunks and evicts distant ones.
    /// No-op if the focus chunk has not changed.
    /// </summary>
    public void UpdateFocus(int worldX, int worldY)
    {
        int cx = worldX / _chunkSize;
        int cy = worldY / _chunkSize;

        if (_initialised && cx == _focusCX && cy == _focusCY)
            return;

        _focusCX = cx;
        _focusCY = cy;
        _initialised = true;

        var desired = BuildResidentSet(cx, cy);

        // Load chunks that entered the resident window
        foreach (var key in desired)
        {
            if (!_chunks.ContainsKey(key))
                EnsureLoaded(key.Item1, key.Item2);
        }

        // Evict chunks that left the resident window
        var toEvict = new List<(int, int)>();
        foreach (var key in _chunks.Keys)
        {
            if (!desired.Contains(key))
                toEvict.Add(key);
        }
        foreach (var key in toEvict)
            Evict(key.Item1, key.Item2);
    }

    /// <summary>
    /// Return a resident chunk, or null if it is not loaded.
    /// Does NOT trigger generation -- use GetOrGenerate for that.
    /// </summary>
    public WorldChunk? GetChunk(int cx, int cy)
    {
        _chunks.TryGetValue((cx, cy), out var chunk);
        return chunk;
    }

    /// <summary>
    /// Return the chunk, generating it on-demand if necessary.
    /// Useful during world init or river/volcano stamping passes that may
    /// need chunks outside the normal resident window.
    /// </summary>
    public WorldChunk GetOrGenerate(int cx, int cy)
    {
        if (_chunks.TryGetValue((cx, cy), out var existing))
            return existing;
        return EnsureLoaded(cx, cy);
    }

    public bool IsResident(int cx, int cy)
        => _chunks.ContainsKey((cx, cy));

    public bool IsSimActive(int cx, int cy)
    {
        int dx = Math.Abs(cx - _focusCX);
        int dy = Math.Abs(cy - _focusCY);
        return dx <= _simRadius && dy <= _simRadius;
    }

    public IEnumerable<WorldChunk> GetResidentChunks()
        => _chunks.Values;

    public IEnumerable<WorldChunk> GetSimActiveChunks()
    {
        foreach (var (key, chunk) in _chunks)
        {
            if (IsSimActive(key.Item1, key.Item2))
                yield return chunk;
        }
    }

    public int ResidentCount => _chunks.Count;

    // -- Helpers ---------------------------------------------------------

    private HashSet<(int, int)> BuildResidentSet(int focusCX, int focusCY)
    {
        int chunksWide = _worldWidth / _chunkSize;
        int chunksTall = _worldHeight / _chunkSize;

        var set = new HashSet<(int, int)>();
        for (int dy = -_residentRadius; dy <= _residentRadius; dy++)
        {
            for (int dx = -_residentRadius; dx <= _residentRadius; dx++)
            {
                int cx = focusCX + dx;
                int cy = focusCY + dy;
                if (cx >= 0 && cx < chunksWide && cy >= 0 && cy < chunksTall)
                    set.Add((cx, cy));
            }
        }
        return set;
    }

    private WorldChunk EnsureLoaded(int cx, int cy)
    {
        // Try persistence hook first
        WorldChunk? chunk = LoadChunk?.Invoke(cx, cy);

        // Fall back to procedural generation
        if (chunk == null)
        {
            if (GenerateChunk == null)
                throw new InvalidOperationException(
                    "ChunkManager.GenerateChunk delegate must be set before calling UpdateFocus.");
            chunk = GenerateChunk(cx, cy);
        }

        _chunks[(cx, cy)] = chunk;
        return chunk;
    }

    private void Evict(int cx, int cy)
    {
        if (!_chunks.TryGetValue((cx, cy), out var chunk))
            return;

        if (chunk.IsDirty)
            SaveChunk?.Invoke(chunk);

        _chunks.Remove((cx, cy));
    }
}