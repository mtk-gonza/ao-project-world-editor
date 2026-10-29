using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapValidationTests
{
    [Fact]
    public void Validator_reports_invalid_entities_transfers_and_isolated_free_tiles()
    {
        var map = new WorldMap();
        map.Blocks[5, 5].NpcIndex = 99;
        map.Blocks[6, 6].TileExit.Map = 3; map.Blocks[6, 6].TileExit.X = 0;
        map.Blocks[10, 10].Blocked = 0; map.Blocks[9, 10].Blocked = 0xF; map.Blocks[11, 10].Blocked = 0xF; map.Blocks[10, 9].Blocked = 0xF; map.Blocks[10, 11].Blocked = 0xF;
        var issues = new MapValidationService().Validate(map, new ObjectData[1], new NpcData[1]);

        Assert.Contains(issues, issue => issue.X == 5 && issue.Message.Contains("NPC"));
        Assert.Contains(issues, issue => issue.X == 6 && issue.Message.Contains("Traslado"));
        Assert.Contains(issues, issue => issue.X == 10 && issue.Message.Contains("bloqueo"));
    }
}
