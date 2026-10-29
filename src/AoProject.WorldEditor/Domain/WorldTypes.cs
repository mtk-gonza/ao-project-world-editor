namespace AoProject.WorldEditor.Domain;

/// <summary>Port of modDeclaraciones.bas data contracts.</summary>
internal enum Heading : byte
{
    North = 1,
    East = 2,
    South = 3,
    West = 4
}

internal readonly record struct Position(short X, short Y);

internal struct GraphicAnimation
{
    public int GraphicIndex { get; set; }
    public float FrameCounter { get; set; }
    public float Speed { get; set; }
    public byte Started { get; set; }
    public short Loops { get; set; }
    public float Angle { get; set; }
}

internal sealed class GraphicData
{
    public short SourceX { get; set; }
    public short SourceY { get; set; }
    public int FileNumber { get; set; }
    public short PixelWidth { get; set; }
    public short PixelHeight { get; set; }
    public float TileWidth { get; set; }
    public float TileHeight { get; set; }
    public int[] Frames { get; set; } = [];
    public float Speed { get; set; }
    public bool Active { get; set; }
    public int MiniMapColor { get; set; }
}

internal sealed class LightData
{
    public short Range { get; set; }
    public int Color { get; set; }
}

internal sealed class ObjectReference
{
    public short Index { get; set; }
    public short Amount { get; set; }
}

internal sealed class WorldPosition
{
    public short Map { get; set; }
    public short X { get; set; }
    public short Y { get; set; }
}

/// <summary>Metadata stored with a map.</summary>
internal sealed class MapInfo
{
    public string Music { get; set; } = string.Empty;
    public int MusicNumberHigh { get; set; }
    public int MusicNumberLow { get; set; }
    public string Name { get; set; } = string.Empty;
    public short MapVersion { get; set; }
    public bool PlayerKilling { get; set; }
    public byte MagicDisabled { get; set; }
    public byte InvisibilityDisabled { get; set; }
    public byte ResurrectionDisabled { get; set; }
    public byte NoEncryptMap { get; set; }
    public string Terrain { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string Restriction { get; set; } = string.Empty;
    public byte Backup { get; set; }
    public byte Safety { get; set; }
    public string Ambient { get; set; } = string.Empty;
    public int LetterGraphic { get; set; }
    public int Level { get; set; }
    public int Extra2 { get; set; }
    public string Exit { get; set; } = string.Empty;
    public bool Changed { get; set; }
    public string Light { get; set; } = string.Empty;
}

/// <summary>Mutable equivalent of the VB6 MapBlock structure.</summary>
internal sealed class MapBlock
{
    public GraphicAnimation[] Graphics { get; } = new GraphicAnimation[8];
    public short CharacterIndex { get; set; }
    public GraphicAnimation ObjectGraphic { get; set; }
    public int[] LightValues { get; } = new int[4];
    public LightData Light { get; } = new();
    public int[] Colors { get; } = new int[4];
    public short ParticleGroup { get; set; }
    public int ParticleIndex { get; set; }
    public bool Marked { get; set; }
    public short NpcIndex { get; set; }
    public ObjectReference Object { get; } = new();
    public WorldPosition TileExit { get; } = new();
    public byte Blocked { get; set; }
    public short Trigger { get; set; }
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }
}

internal sealed class WorldMap
{
    public const int Width = 100;
    public const int Height = 100;

    public MapInfo Info { get; } = new();
    public CsmMapSize CsmMapSize { get; set; } = CsmMapSize.Default;
    public MapBlock[,] Blocks { get; } = CreateBlocks();
    public LegacyMapHeader Header { get; set; } = LegacyMapHeader.Empty;
    public MapEnvironment Environment { get; } = new();

    private static MapBlock[,] CreateBlocks()
    {
        var blocks = new MapBlock[Width + 1, Height + 1];
        for (var x = 1; x <= Width; x++)
        for (var y = 1; y <= Height; y++)
            blocks[x, y] = new MapBlock();
        return blocks;
    }
}

internal readonly record struct CsmMapSize(short XMax, short XMin, short YMax, short YMin)
{
    public static CsmMapSize Default => new(100, 1, 100, 1);

    public bool IsValid => XMin >= 1 && XMax <= WorldMap.Width && XMin <= XMax &&
        YMin >= 1 && YMax <= WorldMap.Height && YMin <= YMax;

    public CsmMapSize Normalize() => IsValid ? this : Default;
}

internal sealed class MapEnvironment
{
    public byte Rain { get; set; }
    public byte Snow { get; set; }
    public byte Fog { get; set; }
    public int BaseLight { get; set; }
}

internal sealed class Ambientation
{
    public string Name { get; init; } = string.Empty;
    public byte Type { get; init; }
    public int GraphicIndex { get; init; }
}

internal sealed class SurfaceData
{
    public string Name { get; set; } = string.Empty;
    public int GraphicIndex { get; set; }
    public byte Width { get; set; }
    public byte Height { get; set; }
    public bool Blocked { get; set; }
    public byte Layer { get; set; }
}

internal sealed class ObjectData
{
    public string Name { get; set; } = string.Empty;
    public short ObjectType { get; set; }
    public int GraphicIndex { get; set; }
    public short SecondaryGraphicIndex { get; set; }
    public string Info { get; set; } = string.Empty;
    public short ClothingIndex { get; set; }
    public short WeaponAnimation { get; set; }
    public short ShieldAnimation { get; set; }
    public string Text { get; set; } = string.Empty;
    public byte Closed { get; set; }
    public byte Subtype { get; set; }
}

internal sealed class NpcData
{
    public string Name { get; set; } = string.Empty;
    public short Body { get; set; }
    public short Head { get; set; }
    public byte Heading { get; set; }
    public short Weapon { get; set; }
    public short Shield { get; set; }
    public short Helmet { get; set; }
}

internal sealed class TriggerData
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
}

internal sealed class DirectionalAnimation
{
    public GraphicAnimation[] Walk { get; } = new GraphicAnimation[5];
    public Position HeadOffset { get; set; }
}

internal sealed class HeadIndexData
{
    public GraphicAnimation[] Head { get; } = new GraphicAnimation[5];
}

internal sealed class CharacterIndices
{
    public DirectionalAnimation[] Bodies { get; init; } = [];
    public HeadIndexData[] Heads { get; init; } = [];
}

internal sealed class BodyMold
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int[] DirectionFrameCounts { get; init; } = new int[5];
}

internal sealed record LegacyMapHeader(
    short MapVersion,
    byte[] Description,
    int Crc,
    int MagicWord,
    short[] MapReserved,
    short[] InfReserved)
{
    public static LegacyMapHeader Empty => new(
        1,
        new byte[255],
        0,
        0,
        new short[4],
        new short[5]);
}
