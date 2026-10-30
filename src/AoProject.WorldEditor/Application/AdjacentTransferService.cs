using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct AdjacentTransfer(
    bool Apply,
    short Map,
    short TargetX,
    short TargetY,
    bool Horizontal,
    short FixedCoordinate,
    short StartCoordinate,
    short EndCoordinate);

/// <summary>Port of frmUnionAdyacente.cmdAplicar_Click.</summary>
internal sealed class AdjacentTransferService
{
    public static AdjacentBounds BasicBounds => new(91, 10, 88, 13, 90, 11, 14, 87);
    public static AdjacentBounds LegalBounds => new(91, 10, 88, 13, 90, 11, 14, 87);
    public static AdjacentBounds UllathorpeBounds => new(94, 7, 92, 9, 93, 8, 10, 91);

    public short[] DetectExistingMaps(WorldSector map, AdjacentBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(map);
        Validate(bounds);
        return [FindHorizontal(map, bounds.TopY, bounds.LeftX + 1, bounds.RightX - 1),
            FindVertical(map, bounds.RightX, bounds.TopY + 1, bounds.BottomY - 1),
            FindHorizontal(map, bounds.BottomY, bounds.LeftX + 1, bounds.RightX - 1),
            FindVertical(map, bounds.LeftX, bounds.TopY + 1, bounds.BottomY - 1)];
    }

    public int Apply(WorldSector map, IReadOnlyList<AdjacentTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (transfers.Count != 4) throw new ArgumentException("Se requieren cuatro lados.", nameof(transfers));
        var changed = 0;
        foreach (var transfer in transfers)
        {
            if (!transfer.Apply) continue;
            Validate(transfer);
            var start = Math.Min(transfer.StartCoordinate, transfer.EndCoordinate);
            var end = Math.Max(transfer.StartCoordinate, transfer.EndCoordinate);
            for (var coordinate = start; coordinate <= end; coordinate++)
            {
                var x = transfer.Horizontal ? coordinate : transfer.FixedCoordinate;
                var y = transfer.Horizontal ? transfer.FixedCoordinate : coordinate;
                var block = map.Blocks[x, y];
                if ((block.Blocked & 0xF) == 0xF) continue;
                if (block.TileExit.Map != transfer.Map || block.TileExit.X != transfer.TargetX + (transfer.Horizontal ? coordinate - start : 0) || block.TileExit.Y != transfer.TargetY + (!transfer.Horizontal ? coordinate - start : 0))
                    changed++;
                block.TileExit.Map = transfer.Map;
                if (transfer.Map == 0)
                { block.TileExit.X = 0; block.TileExit.Y = 0; }
                else
                {
                    block.TileExit.X = transfer.Horizontal ? coordinate : transfer.TargetX;
                    block.TileExit.Y = transfer.Horizontal ? transfer.TargetY : coordinate;
                }
            }
        }
        if (changed > 0) map.Info.Changed = true;
        return changed;
    }

    private static void Validate(AdjacentTransfer transfer)
    {
        if (transfer.Map < 0 || transfer.FixedCoordinate is < 1 or > 100 || transfer.StartCoordinate is < 1 or > 100 || transfer.EndCoordinate is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(transfer));
        if (transfer.Map != 0 && (transfer.TargetX is < 1 or > 100 || transfer.TargetY is < 1 or > 100))
            throw new ArgumentOutOfRangeException(nameof(transfer), "El destino debe estar dentro del mapa.");
    }

    private static void Validate(AdjacentBounds bounds)
    {
        if (bounds.TopY < 1 || bounds.BottomY > WorldSector.Height || bounds.LeftX < 1 || bounds.RightX > WorldSector.Width || bounds.TopY >= bounds.BottomY || bounds.LeftX >= bounds.RightX)
            throw new ArgumentOutOfRangeException(nameof(bounds));
    }
    private static short FindHorizontal(WorldSector map, int y, int start, int end)
    {
        for (var x = start; x <= end; x++) if (map.Blocks[x, y].TileExit.Map != 0) return map.Blocks[x, y].TileExit.Map;
        return 0;
    }
    private static short FindVertical(WorldSector map, int x, int start, int end)
    {
        for (var y = start; y <= end; y++) if (map.Blocks[x, y].TileExit.Map != 0) return map.Blocks[x, y].TileExit.Map;
        return 0;
    }
}

internal readonly record struct AdjacentBounds(short BottomY, short TopY, short RightX, short LeftX, short TopTargetY, short BottomTargetY, short RightTargetX, short LeftTargetX);
