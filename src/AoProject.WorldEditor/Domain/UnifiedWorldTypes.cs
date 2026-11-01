namespace AoProject.WorldEditor.Domain;

/// <summary>
/// Runtime representation of the migrated world. It is one rectangular map;
/// legacy SectorId values are consumed only by the compiler and never identify a
/// tile in this model.
/// </summary>
internal sealed class UnifiedWorldMap
{
    public UnifiedWorldMap(int width, int height, int floorZ = 0)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        FloorZ = floorZ;
        var count = checked(width * height);
        GraphicIndices = new int[checked(count * 8)];
        LightValues = new int[checked(count * 4)];
        Colors = new int[checked(count * 4)];
        Blocked = new byte[count];
        Trigger = new short[count];
        CharacterIndex = new short[count];
        NpcIndex = new short[count];
        ParticleGroup = new short[count];
        ParticleIndex = new int[count];
        Marked = new bool[count];
        ObjectIndex = new short[count];
        ObjectAmount = new short[count];
        ObjectGraphicIndex = new int[count];
        LightRange = new short[count];
        LightColor = new int[count];
        Red = new byte[count];
        Green = new byte[count];
        Blue = new byte[count];
        RoofZoneId = new int[count];
        ExitIndex = new int[count];
        AmbientLight = new int[count];
        Rain = new byte[count];
        Snow = new byte[count];
        Fog = new byte[count];
        Water = new bool[count];
        Lava = new bool[count];
    }

    public int Width { get; }
    public int Height { get; }
    public int FloorZ { get; }
    public int[] GraphicIndices { get; }
    public int[] LightValues { get; }
    public int[] Colors { get; }
    public byte[] Blocked { get; }
    public short[] Trigger { get; }
    public short[] CharacterIndex { get; }
    public short[] NpcIndex { get; }
    public short[] ParticleGroup { get; }
    public int[] ParticleIndex { get; }
    public bool[] Marked { get; }
    public short[] ObjectIndex { get; }
    public short[] ObjectAmount { get; }
    public int[] ObjectGraphicIndex { get; }
    public short[] LightRange { get; }
    public int[] LightColor { get; }
    public byte[] Red { get; }
    public byte[] Green { get; }
    public byte[] Blue { get; }
    public int[] RoofZoneId { get; }
    public int[] ExitIndex { get; }
    public int[] AmbientLight { get; }
    public byte[] Rain { get; }
    public byte[] Snow { get; }
    public byte[] Fog { get; }
    public bool[] Water { get; }
    public bool[] Lava { get; }

    public List<UnifiedWorldExit> Exits { get; } = [];
    public List<UnifiedWorldRoofZone> RoofZones { get; } = [];
    public List<UnifiedWorldEnvironmentProfile> EnvironmentProfiles { get; } = [];
    public List<UnifiedWorldNpcPlacement> Npcs { get; } = [];
    public List<UnifiedWorldParticleGroup> ParticleGroups { get; } = [];
    public List<UnifiedWorldConflict> Conflicts { get; } = [];

    public int Offset(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        return checked(y * Width + x);
    }

    public int GraphicOffset(int tileOffset, int layer) => checked(tileOffset * 8 + layer);
    public int CornerOffset(int tileOffset, int corner) => checked(tileOffset * 4 + corner);
}

internal sealed class UnifiedWorldBundle
{
    private readonly SortedDictionary<int, UnifiedWorldMap> _floors = [];

    public IReadOnlyDictionary<int, UnifiedWorldMap> Floors => _floors;
    public int? ActiveFloorZ { get; private set; }
    public IReadOnlyDictionary<short, GlobalSectorPlacement> Placements { get; private set; } =
        new Dictionary<short, GlobalSectorPlacement>();
    public IReadOnlyDictionary<short, int> SectorFloors { get; private set; } =
        new Dictionary<short, int>();
    public List<UnifiedWorldSectorMetadata> SectorMetadata { get; } = [];
    /// <summary>Validated world settlements manifest embedded in the bundle.</summary>
    public byte[] Settlements { get; set; } = [];
    public byte[] Sector { get; set; } = [];
    public byte[] UnderworldPool { get; set; } = [];
    public byte[] PhoenixPool { get; set; } = [];
    // Raw legacy DAT payloads. They are import-only inputs used to build the
    // canonical runtime-data/*.json catalogs and are never serialized into
    // bundle.aog (v6).
    public byte[] Events { get; set; } = [];
    public byte[] Treasures { get; set; } = [];
    public byte[] Quests { get; set; } = [];
    public byte[] Npcs { get; set; } = [];
    public byte[] Objects { get; set; } = [];
    public byte[] Castles { get; set; } = [];
    public byte[] Spells { get; set; } = [];
    public byte[] EffectsOverTime { get; set; } = [];
    public byte[] GlobalQuests { get; set; } = [];
    /// <summary>INI sources of the scenario catalog, keyed by file name.</summary>
    public IReadOnlyDictionary<string, byte[]> Scenarios { get; set; } = new Dictionary<string, byte[]>();
    public byte[] FactionRanks { get; set; } = [];
    public byte[] FactionRewards { get; set; } = [];
    /// <summary>CraftingJobLists DAT sources, keyed by file name.</summary>
    public IReadOnlyDictionary<string, byte[]> CraftingLists { get; set; } = new Dictionary<string, byte[]>();
    public byte[] InitialEquipment { get; set; } = [];
    /// <summary>Fishing DAT sources (pesca.dat, RecursosEspeciales.dat), keyed by file name.</summary>
    public IReadOnlyDictionary<string, byte[]> Fishing { get; set; } = new Dictionary<string, byte[]>();

    public void SetPlacements(IEnumerable<GlobalSectorPlacement> placements)
    {
        Placements = placements.ToDictionary(item => item.SectorId);
    }

    public void SetSectorFloors(IEnumerable<KeyValuePair<short, int>> sectorFloors)
    {
        SectorFloors = sectorFloors.ToDictionary(item => item.Key, item => item.Value);
    }

    public void Add(UnifiedWorldMap floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        if (!_floors.TryAdd(floor.FloorZ, floor))
            throw new InvalidDataException($"El piso Z={floor.FloorZ} está duplicado.");
        ActiveFloorZ ??= floor.FloorZ;
    }

    public UnifiedWorldMap Select(int floorZ)
    {
        if (!_floors.TryGetValue(floorZ, out var floor))
            throw new KeyNotFoundException($"No existe el piso Z={floorZ}.");
        ActiveFloorZ = floorZ;
        return floor;
    }
}

/// <summary>Complete source-map metadata retained alongside the compact floors.</summary>
internal sealed class UnifiedWorldSectorMetadata
{
    public short SectorId { get; init; }
    public int FloorZ { get; init; }
    public CsmSectorSize MapSize { get; init; } = CsmSectorSize.Default;
    public SectorInfo Info { get; } = new();
    public MapEnvironment Environment { get; } = new();
}

internal enum UnifiedWorldExitKind
{
    Internal,
    CrossFloor,
    External
}

internal sealed record UnifiedWorldExit(
    UnifiedWorldExitKind Kind,
    int SourceX,
    int SourceY,
    int TargetX,
    int TargetY,
    short ExternalSectorId = 0,
    short ExternalX = 0,
    short ExternalY = 0,
    int SourceZ = 0,
    int TargetZ = 0,
    int ExternalZ = 0);

internal sealed record UnifiedWorldRoofZone(int Id, int Trigger, int MinX, int MinY, int MaxX, int MaxY, int Z = 0);

internal sealed record UnifiedWorldEnvironmentProfile(
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    int BaseLight,
    byte Rain,
    byte Snow,
    byte Fog,
    string Music,
    string Zone,
    string Restriction,
    byte Safety,
    int Z = 0,
    int LegacyLevel = 0);

internal sealed record UnifiedWorldConflict(
    int GlobalX,
    int GlobalY,
    short FirstSectorId,
    short SecondSectorId,
    string Field,
    string Message,
    int Z = 0);

internal sealed record UnifiedWorldNpcPlacement(
    int X,
    int Y,
    short Body,
    short Head,
    byte Heading,
    short Weapon,
    short Shield,
    short Helmet,
    int Z = 0);

internal sealed record UnifiedWorldParticleGroup(int Index, int X, int Y, int Type, int Z = 0);
