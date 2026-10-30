using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class ResourceParityTests
{
    private static string ResourceRoot =>
        Environment.GetEnvironmentVariable("AO_RESOURCES") ??
        (Directory.Exists(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "Recursos")))
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "Recursos"))
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ao-project-resources")));

    [Fact]
    public void GraphicIndexLoader_reads_reference_resources()
    {
        var graphics = new GraphicIndexLoader().Load(ResourceRoot);

        Assert.True(graphics.Count > 60_000);
        Assert.Contains(graphics.Skip(1), item => item.Active && item.FileNumber > 0);
    }

    [Fact]
    public void ReferenceIndexLoader_reads_editor_indices()
    {
        var loader = new ReferenceIndexLoader();

        var surfaces = loader.LoadSurfaces(ResourceRoot);
        var objects = loader.LoadObjects(ResourceRoot);
        var npcs = loader.LoadNpcs(ResourceRoot);
        var triggers = loader.LoadTriggers(ResourceRoot);

        Assert.True(surfaces.Count > 12_000);
        Assert.True(objects.Count > 6_000);
        Assert.True(npcs.Count > 0);
        Assert.Equal(20, triggers.Count);
    }

    [Fact]
    public void CsmMapFileService_reads_map_one_without_writing_resources()
    {
        var mapPath = Path.Combine(ResourceRoot, "Mapas", "mapa1.csm");
        var map = new SectorCsmFileService().Load(mapPath);

        Assert.Equal("Ciudad de Ullathorpe", map.Info.Name);
        Assert.Equal(100, WorldSector.Width);
        Assert.Equal(100, WorldSector.Height);
        Assert.Contains(map.Blocks.Cast<SectorTile>().Where(block => block is not null), block => block.Graphics[0].GraphicIndex > 0);
        var geometry = MapRenderGeometry.Build(map);
        Assert.NotEmpty(geometry.Tiles);
        Assert.All(geometry.Tiles, tile => Assert.InRange(tile.Layer, 0, 3));
    }

    [Fact]
    public void GraphicSourceResolver_resolves_a_real_png()
    {
        var graphics = new GraphicIndexLoader().Load(ResourceRoot);
        var graphic = graphics.Skip(1).First(item => item.Active && item.FileNumber > 0);
        var source = new GraphicSourceResolver(ResourceRoot).Resolve(graphic);

        Assert.True(File.Exists(source.Path));
        Assert.True(source.Width > 0);
        Assert.True(source.Height > 0);
    }

    [Fact]
    public void GraphicAtlas_packs_a_real_graphic_region()
    {
        var graphics = new GraphicIndexLoader().Load(ResourceRoot);
        var graphic = graphics.Skip(1).First(item => item.Active && item.FileNumber > 0 && item.PixelWidth > 0);
        using var atlas = new GraphicAtlas(ResourceRoot);

        Assert.True(atlas.TryResolve(graphic, out var region));
        Assert.Single(atlas.Pages);
        Assert.Equal(0, region.Page);
        Assert.True(region.U1 > region.U0);
        Assert.True(region.V1 > region.V0);
    }

    [Fact]
    public void MapEditingService_sets_and_undoes_a_graphic()
    {
        var map = new SectorCsmFileService().Load(Path.Combine(ResourceRoot, "Mapas", "mapa1.csm"));
        var editing = new MapEditingService();
        var old = map.Blocks[1, 1].Graphics[0].GraphicIndex;

        editing.SetGraphic(map, 1, 1, 0, 123);
        Assert.Equal(123, map.Blocks[1, 1].Graphics[0].GraphicIndex);
        Assert.True(map.Info.Changed);
        Assert.True(editing.Undo(map));
        Assert.Equal(old, map.Blocks[1, 1].Graphics[0].GraphicIndex);
    }

    [Fact]
    public void CsmMapFileService_roundtrips_to_a_temp_file()
    {
        var service = new SectorCsmFileService();
        var source = service.Load(Path.Combine(ResourceRoot, "Mapas", "mapa1.csm"));
        var target = Path.Combine(Path.GetTempPath(), $"world-editor-{Guid.NewGuid():N}.csm");
        try
        {
            service.Save(source, target);
            var copy = service.Load(target);
            Assert.Equal(source.Info.Name, copy.Info.Name);
            Assert.Equal(source.Blocks[1, 1].Graphics[0].GraphicIndex, copy.Blocks[1, 1].Graphics[0].GraphicIndex);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)] [InlineData(15)]
    public void CsmMapFileService_roundtrips_each_vb6_directional_mask(byte mask)
    {
        var service = new SectorCsmFileService();
        var source = new WorldSector();
        source.Blocks[12, 13].Blocked = mask;
        var target = Path.Combine(Path.GetTempPath(), $"world-editor-blocked-{Guid.NewGuid():N}.csm");
        try
        {
            service.Save(source, target);
            Assert.Equal(mask, service.Load(target).Blocks[12, 13].Blocked);
        }
        finally { File.Delete(target); }
    }

    [Fact]
    public void CsmMapFileService_preserves_environment_metadata()
    {
        var service = new SectorCsmFileService();
        var source = service.Load(Path.Combine(ResourceRoot, "Mapas", "mapa1.csm"));
        source.Environment.BaseLight = 321;
        source.Environment.Rain = 4;
        source.Environment.Snow = 5;
        source.Environment.Fog = 6;
        var target = Path.Combine(Path.GetTempPath(), $"world-editor-{Guid.NewGuid():N}.csm");
        try
        {
            service.Save(source, target);
            var copy = service.Load(target);
            Assert.Equal(321, copy.Environment.BaseLight);
            Assert.Equal((byte)4, copy.Environment.Rain);
            Assert.Equal((byte)5, copy.Environment.Snow);
            Assert.Equal((byte)6, copy.Environment.Fog);
        }
        finally { File.Delete(target); }
    }

    [Fact]
    public void Legacy_v3_roundtrip_persists_edited_map_version()
    {
        var sourcePath = Path.Combine(ResourceRoot, "Mapas", "mapa1.map");
        if (!File.Exists(sourcePath)) return;
        var service = new LegacySectorFileService();
        var source = service.LoadV3(sourcePath);
        source.Info.MapVersion = 123;
        var target = Path.Combine(Path.GetTempPath(), $"world-editor-{Guid.NewGuid():N}.map");
        try
        {
            service.SaveV3(source, target);
            Assert.Equal((short)123, service.LoadV3(target).Info.MapVersion);
        }
        finally { File.Delete(target); File.Delete(Path.ChangeExtension(target, ".inf")); File.Delete(Path.ChangeExtension(target, ".dat")); }
    }
}
