using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class TransferShiftTests
{
    [Fact]
    public void Transfer_shift_moves_border_payloads_and_updates_destination_coordinates()
    {
        var map = new WorldSector();
        map.Blocks[20, 10].TileExit.SectorId = 7; map.Blocks[20, 10].TileExit.X = 22; map.Blocks[20, 10].TileExit.Y = 23;
        var shift = new TransferShift(10, 11, 90, 91, 90, 91, 11, 12, 88, 89, 12, 13);
        var moved = new TransferShiftService().Shift(map, shift);

        Assert.Equal(1, moved);
        Assert.Equal((short)7, map.Blocks[20, 11].TileExit.SectorId);
        Assert.Equal((short)90, map.Blocks[20, 11].TileExit.Y);
        Assert.Equal((short)0, map.Blocks[20, 10].TileExit.SectorId);
    }
}
