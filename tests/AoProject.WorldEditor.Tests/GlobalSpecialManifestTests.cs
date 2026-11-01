using System.Text.Json;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalSpecialManifestTests
{
    [Fact]
    public void Underworld_portals_are_exported_as_global_positions()
    {
        var bundle = new UnifiedWorldBundle();
        bundle.Add(new UnifiedWorldMap(100, 100, 0));
        bundle.SetPlacements([new GlobalSectorPlacement(10, new GlobalChunkCoordinate(0, 0), "surface")]);
        bundle.SetSectorFloors([new KeyValuePair<short, int>(10, 0)]);
        var source = "[Portal1]\nSourceMap=10\nSourceX=2\nSourceY=3\nDestinationMap=10\nDestinationX=4\nDestinationY=5\n"u8.ToArray();

        using var json = JsonDocument.Parse(new GlobalSpecialManifestService().TransformUnderworld(source, bundle));
        var position = json.RootElement.GetProperty("portals")[0].GetProperty("source");
        Assert.Equal(0, position.GetProperty("floorZ").GetInt32());
        Assert.Equal(1, position.GetProperty("globalX").GetInt32());
        Assert.Equal(2, position.GetProperty("globalY").GetInt32());
        Assert.False(position.TryGetProperty("sectorId", out _));
    }
}
