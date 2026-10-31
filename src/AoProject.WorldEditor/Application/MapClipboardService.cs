using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct TileSelection(int StartX, int StartY, int EndX, int EndY)
{
    public int Width => Math.Abs(EndX - StartX) + 1;
    public int Height => Math.Abs(EndY - StartY) + 1;
    public TileSelection Normalize() => new(Math.Min(StartX, EndX), Math.Min(StartY, EndY), Math.Max(StartX, EndX), Math.Max(StartY, EndY));
}

/// <summary>Copy/paste and rectangular selection operations from the block tools.</summary>
internal sealed class MapClipboardService
{
    private ClipboardData? _clipboard;

    public bool HasClipboard => _clipboard is not null;
    public (int Width, int Height)? ClipboardSize =>
        _clipboard is null ? null : (_clipboard.Width, _clipboard.Height);

    public void Copy(WorldSector map, TileSelection selection)
    {
        var normalized = selection.Normalize();
        Validate(map, normalized);
        var blocks = new SectorTile[normalized.Width, normalized.Height];
        for (var x = 0; x < normalized.Width; x++)
        for (var y = 0; y < normalized.Height; y++)
            blocks[x, y] = Clone(map.Blocks[normalized.StartX + x, normalized.StartY + y]);
        _clipboard = new ClipboardData(blocks);
    }

    public void Paste(WorldSector map, int targetX, int targetY)
    {
        if (_clipboard is null) throw new InvalidOperationException("No hay un bloque copiado.");
        for (var x = 0; x < _clipboard.Width; x++)
        for (var y = 0; y < _clipboard.Height; y++)
        {
            var destinationX = targetX + x;
            var destinationY = targetY + y;
            if (!InDomain(map, destinationX, destinationY)) continue;
            Copy(_clipboard.Blocks[x, y], map.Blocks[destinationX, destinationY]);
            map.Blocks[destinationX, destinationY].NpcIndex = 0;
            map.Blocks[destinationX, destinationY].CharacterIndex = 0;
        }
        map.Info.Changed = true;
    }

    public void Cut(WorldSector map, TileSelection selection)
    {
        var normalized = selection.Normalize();
        Validate(map, normalized);
        Copy(map, normalized);
        for (var x = normalized.StartX; x <= normalized.EndX; x++)
        for (var y = normalized.StartY; y <= normalized.EndY; y++) map.Blocks[x, y] = new SectorTile();
        map.Info.Changed = true;
    }

    public void CopyGlobal(GlobalWorldDocument world, GlobalTileSelection selection)
    {
        var normalized = selection.Normalize();
        var blocks = new SectorTile[normalized.EndX - normalized.StartX + 1, normalized.EndY - normalized.StartY + 1];
        for (var x = normalized.StartX; x <= normalized.EndX; x++)
        for (var y = normalized.StartY; y <= normalized.EndY; y++)
        {
            var block = new SectorTile();
            if (world.TryResolve(x, y, out var location))
                Copy(world.Maps[location.SectorId].Blocks[location.LocalX, location.LocalY], block);
            blocks[x - normalized.StartX, y - normalized.StartY] = block;
        }
        _clipboard = new ClipboardData(blocks);
    }

    public int PasteGlobal(GlobalWorldDocument world, int targetX, int targetY)
    {
        if (_clipboard is null) throw new InvalidOperationException("No hay un bloque copiado.");
        var changed = 0;
        for (var x = 0; x < _clipboard.Width; x++)
        for (var y = 0; y < _clipboard.Height; y++)
        {
            if (!world.TryResolve(targetX + x, targetY + y, out var location)) continue;
            Copy(_clipboard.Blocks[x, y], world.Maps[location.SectorId].Blocks[location.LocalX, location.LocalY]);
            var block = world.Maps[location.SectorId].Blocks[location.LocalX, location.LocalY];
            block.NpcIndex = 0; block.CharacterIndex = 0;
            world.Maps[location.SectorId].Info.Changed = true;
            changed++;
        }
        return changed;
    }

    public int CutGlobal(GlobalWorldDocument world, GlobalTileSelection selection)
    {
        CopyGlobal(world, selection);
        var normalized = selection.Normalize();
        var changed = 0;
        for (var x = normalized.StartX; x <= normalized.EndX; x++)
        for (var y = normalized.StartY; y <= normalized.EndY; y++)
        {
            if (!world.TryResolve(x, y, out var location)) continue;
            world.Maps[location.SectorId].Blocks[location.LocalX, location.LocalY] = new SectorTile();
            world.Maps[location.SectorId].Info.Changed = true;
            changed++;
        }
        return changed;
    }

    private static void Validate(WorldSector map, TileSelection selection)
    {
        if (!InDomain(map, selection.StartX, selection.StartY) ||
            !InDomain(map, selection.EndX, selection.EndY))
            throw new ArgumentOutOfRangeException(nameof(selection));
    }

    private static bool InDomain(WorldSector map, int x, int y) =>
        x >= map.CsmSectorSize.XMin && x <= map.CsmSectorSize.XMax &&
        y >= map.CsmSectorSize.YMin && y <= map.CsmSectorSize.YMax &&
        x is >= 1 and <= WorldSector.Width && y is >= 1 and <= WorldSector.Height;

    private static SectorTile Clone(SectorTile source)
    {
        var destination = new SectorTile(); Copy(source, destination); return destination;
    }

    private static void Copy(SectorTile source, SectorTile destination)
    {
        Array.Copy(source.Graphics, destination.Graphics, source.Graphics.Length);
        destination.ObjectGraphic = source.ObjectGraphic;
        Array.Copy(source.LightValues, destination.LightValues, source.LightValues.Length); Array.Copy(source.Colors, destination.Colors, source.Colors.Length);
        destination.Light.Range = source.Light.Range; destination.Light.Color = source.Light.Color;
        destination.ParticleGroup = source.ParticleGroup; destination.ParticleIndex = source.ParticleIndex; destination.Marked = source.Marked;
        destination.Blocked = source.Blocked; destination.Trigger = source.Trigger; destination.Red = source.Red; destination.Green = source.Green; destination.Blue = source.Blue;
        destination.Object.Index = source.Object.Index; destination.Object.Amount = source.Object.Amount;
        destination.TileExit.SectorId = source.TileExit.SectorId; destination.TileExit.X = source.TileExit.X; destination.TileExit.Y = source.TileExit.Y;
        destination.NpcIndex = source.NpcIndex; destination.CharacterIndex = source.CharacterIndex;
    }

    private sealed class ClipboardData(SectorTile[,] blocks)
    {
        public SectorTile[,] Blocks { get; } = blocks;
        public int Width => Blocks.GetLength(0);
        public int Height => Blocks.GetLength(1);
    }
}
