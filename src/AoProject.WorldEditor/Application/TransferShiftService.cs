using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct TransferShift(
    short CurrentTopY, short ShiftedTopY, short DestinationTopY,
    short CurrentBottomY, short ShiftedBottomY, short DestinationBottomY,
    short CurrentLeftX, short ShiftedLeftX, short DestinationLeftX,
    short CurrentRightX, short ShiftedRightX, short DestinationRightX);

internal sealed class TransferShiftService
{
    public int Shift(WorldSector map, TransferShift shift)
    {
        ArgumentNullException.ThrowIfNull(map);
        Validate(shift);
        var moved = 0;
        for (var x = 13; x <= 87; x++) { Move(map, x, shift.CurrentTopY, x, shift.ShiftedTopY, shift.DestinationTopY, true, ref moved); Move(map, x, shift.CurrentBottomY, x, shift.ShiftedBottomY, shift.DestinationBottomY, true, ref moved); }
        for (var y = 11; y <= 90; y++) { Move(map, shift.CurrentLeftX, y, shift.ShiftedLeftX, y, shift.DestinationLeftX, false, ref moved); Move(map, shift.CurrentRightX, y, shift.ShiftedRightX, y, shift.DestinationRightX, false, ref moved); }
        if (moved > 0) map.Info.Changed = true;
        return moved;
    }

    private static bool Move(WorldSector map, int sourceX, int sourceY, int destinationX, int destinationY, int destinationCoordinate, bool horizontal, ref int moved)
    {
        var source = map.Blocks[sourceX, sourceY];
        if (source.TileExit.Map == 0) return false;
        var destination = map.Blocks[destinationX, destinationY];
        destination.TileExit.Map = source.TileExit.Map;
        destination.TileExit.X = horizontal ? source.TileExit.X : (short)destinationCoordinate;
        destination.TileExit.Y = horizontal ? (short)destinationCoordinate : source.TileExit.Y;
        source.TileExit.Map = 0; source.TileExit.X = 0; source.TileExit.Y = 0; moved++; return true;
    }

    private static void Validate(TransferShift shift)
    {
        foreach (var value in new[] { shift.CurrentTopY, shift.ShiftedTopY, shift.DestinationTopY, shift.CurrentBottomY, shift.ShiftedBottomY, shift.DestinationBottomY, shift.CurrentLeftX, shift.ShiftedLeftX, shift.DestinationLeftX, shift.CurrentRightX, shift.ShiftedRightX, shift.DestinationRightX })
            if (value is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(shift));
    }
}
