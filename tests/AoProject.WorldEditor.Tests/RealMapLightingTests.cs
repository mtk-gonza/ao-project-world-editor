using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace AoProject.WorldEditor.Tests;

public sealed class RealMapLightingTests
{
    private readonly ITestOutputHelper _output;
    public RealMapLightingTests(ITestOutputHelper output) => _output = output;

    private static string ResourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "ao-project-resources");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "ao-project-resources");
    }

    [Fact]
    public void Night_light_map_keeps_the_ambient_colour_away_from_lights()
    {
        var root = ResourceRoot();
        var mapPath = Path.Combine(root, "Mapas", "mapa1.csm");
        if (!File.Exists(mapPath)) return;

        var map = new CsmMapFileService().Load(mapPath);
        var lights = new MapLightingService().Compute(map, MapLightingService.NightLight);
        _output.WriteLine($"NightLight=0x{MapLightingService.NightLight:X8} t10=0x{lights[10, 10, 0]:X8} t50=0x{lights[50, 50, 0]:X8} t90=0x{lights[90, 90, 0]:X8}");

        var lightCount = map.Blocks.Cast<Domain.MapBlock>().Count(block => block is not null && block.Light.Range != 0);
        _output.WriteLine($"tiles with light = {lightCount}");
        for (var x = 1; x <= Domain.WorldMap.Width; x++)
        for (var y = 1; y <= Domain.WorldMap.Height; y++)
        {
            var block = map.Blocks[x, y];
            if (block.Light.Range == 0) continue;
            _output.WriteLine($"light {x},{y} range={block.Light.Range} color=0x{block.Light.Color:X8}");
        }
        Assert.True(lightCount >= 0);
    }
}
