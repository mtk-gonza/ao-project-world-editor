using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapConversionTests
{
    [Fact]
    public void Legacy_maps_are_converted_to_csm()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-convert-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Conversor"); var destination = Path.Combine(root, "out");
        Directory.CreateDirectory(source);
        try
        {
            var map = new WorldSector(); map.Blocks[10, 11].Graphics[0].GraphicIndex = 77;
            new LegacySectorFileService().SaveV3(map, Path.Combine(source, "Mapa1.map"));
            var results = new MapConversionService().ConvertDirectory(source, destination);
            Assert.Single(results); Assert.True(results[0].Converted);
            var converted = new SectorCsmFileService().Load(Path.Combine(destination, "Mapa1.csm"));
            Assert.Equal(77, converted.Blocks[10, 11].Graphics[0].GraphicIndex);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
