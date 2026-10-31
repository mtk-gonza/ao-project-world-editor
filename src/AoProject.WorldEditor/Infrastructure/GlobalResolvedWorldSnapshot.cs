using System.Text;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>
/// Serializes the already assembled world. The 75x81 stride is the same stride
/// used by the visual stitcher; map ids are retained only as source aliases.
/// </summary>
internal static class GlobalResolvedWorldSnapshot
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AOWORLD");
    private const int Version = 2;

    internal sealed record ResolvedTile(
        int X, int Y, byte Blocked, short Trigger, short CharacterIndex, short NpcIndex,
        short ParticleGroup, int ParticleIndex, bool Marked, short ObjectIndex, short ObjectAmount,
        GraphicAnimation ObjectGraphic, short LightRange, int LightColor, int[] LightValues,
        int[] Colors, byte Red, byte Green, byte Blue, int[] Graphics, GraphicAnimation[] GraphicAnimations,
        int ExitX, int ExitY);

    internal sealed record ResolvedSnapshot(int Width, int Height, IReadOnlyList<ResolvedTile> Tiles,
        IReadOnlyList<EmbeddedImage> Images);

    internal sealed record EmbeddedImage(string Name, byte[] Data);

    public static byte[] Serialize(GlobalWorldDocument document,
        IReadOnlyList<EmbeddedImage>? images = null)
    {
        var placements = document.Assembly.Placements.ToDictionary(item => item.SectorId);
        var minChunkX = placements.Count == 0 ? 0 : placements.Values.Min(item => item.Chunk.X);
        var minChunkY = placements.Count == 0 ? 0 : placements.Values.Min(item => item.Chunk.Y);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic); writer.Write(Version);
        writer.Write(document.Assembly.Width * GlobalChunkGeometry.WidthTiles + WorldSector.Width);
        writer.Write(document.Assembly.Height * GlobalChunkGeometry.HeightTiles + WorldSector.Height);

        var tiles = new List<(int X, int Y, SectorTile Block)>();
        var occupied = new HashSet<(int X, int Y)>();
        foreach (var placement in placements.Values.OrderBy(item => item.Chunk.Y).ThenBy(item => item.Chunk.X))
        {
            if (!document.Maps.TryGetValue(placement.SectorId, out var map)) continue;
            var hasWest = placements.Values.Any(item => item.Chunk == new GlobalChunkCoordinate(placement.Chunk.X - 1, placement.Chunk.Y));
            var hasNorth = placements.Values.Any(item => item.Chunk == new GlobalChunkCoordinate(placement.Chunk.X, placement.Chunk.Y - 1));
            for (var y = 1; y <= WorldSector.Height; y++)
            for (var x = 1; x <= WorldSector.Width; x++)
            {
                if (!GlobalChunkGeometry.IsCoreTile(x, y, hasWest, hasNorth)) continue;
                var global = document.GlobalTileForPlacement(placement, x, y, minChunkX, minChunkY);
                var globalX = global.X;
                var globalY = global.Y;
                if (occupied.Add((globalX, globalY))) tiles.Add((globalX, globalY, map.Blocks[x, y]));
            }
        }

        writer.Write(tiles.Count);
        foreach (var tile in tiles)
        {
            writer.Write(tile.X); writer.Write(tile.Y);
            var block = tile.Block;
            writer.Write(block.Blocked); writer.Write(block.Trigger);
            writer.Write(block.CharacterIndex);
            writer.Write(block.NpcIndex); writer.Write(block.ParticleGroup); writer.Write(block.ParticleIndex);
            writer.Write(block.Marked);
            writer.Write(block.Object.Index); writer.Write(block.Object.Amount);
            writer.Write(block.ObjectGraphic.GraphicIndex);
            writer.Write(block.ObjectGraphic.FrameCounter); writer.Write(block.ObjectGraphic.Speed);
            writer.Write(block.ObjectGraphic.Started); writer.Write(block.ObjectGraphic.Loops); writer.Write(block.ObjectGraphic.Angle);
            writer.Write(block.Light.Range); writer.Write(block.Light.Color);
            for (var i = 0; i < block.LightValues.Length; i++) writer.Write(block.LightValues[i]);
            for (var i = 0; i < block.Colors.Length; i++) writer.Write(block.Colors[i]);
            writer.Write(block.Red); writer.Write(block.Green); writer.Write(block.Blue);
            for (var layer = 0; layer < block.Graphics.Length; layer++) writer.Write(block.Graphics[layer].GraphicIndex);
            for (var layer = 0; layer < block.Graphics.Length; layer++)
            {
                var graphic = block.Graphics[layer];
                writer.Write(graphic.FrameCounter); writer.Write(graphic.Speed); writer.Write(graphic.Started);
                writer.Write(graphic.Loops); writer.Write(graphic.Angle);
            }
            var exit = block.TileExit;
            if (exit.SectorId > 0 && placements.TryGetValue(exit.SectorId, out var targetPlacement))
            {
                var target = document.GlobalTileForPlacement(targetPlacement, exit.X, exit.Y, minChunkX, minChunkY);
                var targetX = target.X;
                var targetY = target.Y;
                writer.Write(targetX); writer.Write(targetY);
            }
            else
            {
                writer.Write(-1); writer.Write(-1);
            }
        }
        images ??= [];
        writer.Write(images.Count);
        foreach (var image in images)
        {
            writer.Write(image.Name);
            writer.Write(image.Data.Length);
            writer.Write(image.Data);
        }
        return stream.ToArray();
    }

    public static ResolvedSnapshot Read(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Firma AOWORLD inválida.");
        var version = reader.ReadInt32();
        if (version is not (1 or Version))
            throw new InvalidDataException($"Versión AOWORLD no soportada: {version}.");
        var width = reader.ReadInt32(); var height = reader.ReadInt32();
        var count = reader.ReadInt32();
        if (width <= 0 || height <= 0 || count < 0 || count > width * height)
            throw new InvalidDataException("Dimensiones o cantidad de tiles inválidas en AOWORLD.");
        var tiles = new List<ResolvedTile>(count);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt32(); var y = reader.ReadInt32();
            if (x < 0 || x >= width || y < 0 || y >= height)
                throw new InvalidDataException($"Tile fuera de rango en AOWORLD: {x},{y}.");
            var blocked = reader.ReadByte(); var trigger = reader.ReadInt16();
            var characterIndex = reader.ReadInt16();
            var npc = reader.ReadInt16(); var particleGroup = reader.ReadInt16(); var particle = reader.ReadInt32();
            var marked = reader.ReadBoolean();
            var objectIndex = reader.ReadInt16(); var objectAmount = reader.ReadInt16();
            var objectGraphic = ReadGraphic(reader);
            var lightRange = reader.ReadInt16(); var lightColor = reader.ReadInt32();
            var lightValues = new int[4];
            for (var value = 0; value < lightValues.Length; value++) lightValues[value] = reader.ReadInt32();
            var colors = new int[4];
            for (var value = 0; value < colors.Length; value++) colors[value] = reader.ReadInt32();
            var red = reader.ReadByte(); var green = reader.ReadByte(); var blue = reader.ReadByte();
            var graphics = new int[8];
            for (var layer = 0; layer < graphics.Length; layer++) graphics[layer] = reader.ReadInt32();
            var graphicAnimations = new GraphicAnimation[graphics.Length];
            for (var layer = 0; layer < graphicAnimations.Length; layer++) graphicAnimations[layer] = ReadAnimation(reader, graphics[layer]);
            var exitX = reader.ReadInt32(); var exitY = reader.ReadInt32();
            tiles.Add(new ResolvedTile(x, y, blocked, trigger, characterIndex, npc, particleGroup, particle,
                marked, objectIndex, objectAmount, objectGraphic, lightRange, lightColor, lightValues,
                colors, red, green, blue, graphics, graphicAnimations, exitX, exitY));
        }
        var images = new List<EmbeddedImage>();
        if (version >= 2)
        {
            var imageCount = reader.ReadInt32();
            if (imageCount < 0 || imageCount > 100)
                throw new InvalidDataException("Cantidad de imagenes invalida en AOWORLD.");
            for (var i = 0; i < imageCount; i++)
            {
                var name = reader.ReadString();
                var length = reader.ReadInt32();
                if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new InvalidDataException("Imagen embebida invalida en AOWORLD.");
                images.Add(new EmbeddedImage(name, reader.ReadBytes(length)));
            }
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("AOWORLD contiene datos adicionales no interpretados.");
        return new ResolvedSnapshot(width, height, tiles, images);
    }

    private static GraphicAnimation ReadGraphic(BinaryReader reader)
    {
        return new GraphicAnimation
        {
            GraphicIndex = reader.ReadInt32(),
            FrameCounter = reader.ReadSingle(),
            Speed = reader.ReadSingle(),
            Started = reader.ReadByte(),
            Loops = reader.ReadInt16(),
            Angle = reader.ReadSingle()
        };
    }

    private static GraphicAnimation ReadAnimation(BinaryReader reader, int graphicIndex) => new()
    {
        GraphicIndex = graphicIndex,
        FrameCounter = reader.ReadSingle(),
        Speed = reader.ReadSingle(),
        Started = reader.ReadByte(),
        Loops = reader.ReadInt16(),
        Angle = reader.ReadSingle()
    };
}
