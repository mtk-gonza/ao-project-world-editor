using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapsWorldDataServiceTests
{
    [Fact]
    public void Reads_world_grid_and_places_the_root_world()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[INIT]\nTOTALWORLDS=1\n[WORLDMAP1]\n1=10\n2=11\n3=12\n4=13\nANCHO=2\nALTO=2\n[WORLDMAP2]\n1=20\nANCHO=1\nALTO=1\n");
            var service = new MapsWorldDataService();
            var layouts = service.Load(path);
            var maps = new Dictionary<short, WorldSector>
            {
                [10] = new(), [11] = new(), [12] = new(), [13] = new()
            };

            var assembly = MapsWorldDataService.BuildAssembly(layouts, maps, 10);

            Assert.NotNull(assembly);
            Assert.Equal(2, assembly!.Width);
            Assert.True(assembly.WrapX);
            Assert.Equal(new GlobalChunkCoordinate(1, 0), assembly.Placements.Single(item => item.SectorId == 11).Chunk);
            Assert.Equal("world-1", assembly.Placements.Single(item => item.SectorId == 10).LevelId);
            Assert.Equal([2], service.IgnoredWorldIds);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Preserves_repeated_map_ids_as_distinct_global_chunks()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[INIT]\nTOTALWORLDS=1\n[WORLDMAP1]\n1=10\n2=10\nANCHO=2\nALTO=1\n");
            var layouts = new MapsWorldDataService().Load(path);
            var assembly = MapsWorldDataService.BuildAssembly(layouts, new Dictionary<short, WorldSector> { [10] = new() }, 10);

            Assert.NotNull(assembly);
            Assert.Equal(2, assembly!.Placements.Count);
            Assert.Equal(new GlobalChunkCoordinate(0, 0), assembly.Placements[0].Chunk);
            Assert.Equal(new GlobalChunkCoordinate(1, 0), assembly.Placements[1].Chunk);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Root_skips_repeated_filler_maps()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                "[INIT]\nTOTALWORLDS=1\n[WORLDMAP1]\n1=300\n2=300\n3=401\n4=300\nANCHO=2\nALTO=2\n");
            var layout = new MapsWorldDataService().Load(path).Single();

            var root = MapsWorldDataService.ChooseRoot(layout, new short[] { 300, 401 });

            Assert.Equal(401, root);
        }
        finally { File.Delete(path); }
    }
}
