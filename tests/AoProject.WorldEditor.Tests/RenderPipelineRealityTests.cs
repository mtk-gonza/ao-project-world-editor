using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using AoProject.WorldEditor.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace AoProject.WorldEditor.Tests;

public sealed class RenderPipelineRealityTests
{
    private readonly ITestOutputHelper _output;
    public RenderPipelineRealityTests(ITestOutputHelper output) => _output = output;

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
    public void Real_map_has_graphics_in_the_visible_rows()
    {
        var root = ResourceRoot();
        var mapPath = Path.Combine(root, "Mapas", "mapa1.csm");
        if (!File.Exists(mapPath)) return;

        var map = new CsmMapFileService().Load(mapPath);
        for (var y = 41; y <= 44; y++)
        {
            var filled = Enumerable.Range(1, 100).Count(x => map.Blocks[x, y].Graphics[0].GraphicIndex != 0);
            _output.WriteLine($"row {y}: layer1 tiles = {filled}");
        }
        var total = Enumerable.Range(1, 100).Sum(y => Enumerable.Range(1, 100).Count(x => map.Blocks[x, y].Graphics[0].GraphicIndex != 0));
        _output.WriteLine($"layer1 total = {total}");
        Assert.True(total > 5000, $"El mapa real deberia tener capa 1 poblada (fue {total}).");
    }

    [Fact]
    public void Npc_sprites_are_produced_for_the_reference_map()
    {
        var root = ResourceRoot();
        var mapPath = Path.Combine(root, "Mapas", "mapa1.csm");
        if (!File.Exists(mapPath)) return;

        var catalog = WorldEditorResourceCatalog.Load(root);
        var map = new CsmMapFileService().Load(mapPath);
        var characters = new CharacterListService();
        var created = characters.RefreshAll(map, catalog.Npcs);
        var sprites = new CharacterRenderService().Render(characters.Characters, catalog.Bodies, catalog.Heads);
        var renderable = characters.Characters.Count(character =>
            character.Active && character.Body > 0 && character.Body < catalog.Bodies.Length &&
            catalog.Bodies[character.Body].Walk[1].GraphicIndex > 0);
        _output.WriteLine($"npcs in catalog={catalog.Npcs.Count} bodies={catalog.Bodies.Length} heads={catalog.Heads.Length}");
        _output.WriteLine($"characters created={created} renderable={renderable} sprites={sprites.Count}");
        Assert.True(created > 0, "El mapa de referencia deberia tener NPCs.");
        Assert.True(sprites.Count >= renderable, "Cada NPC con cuerpo definido deberia producir un sprite.");
        _output.WriteLine($"npcs sin cuerpo definido en el recurso: {created - renderable}");
    }

    [Fact]
    public void Overlay_kinds_are_marked_and_separate_from_tiles()
    {
        var map = new WorldMap();
        map.Blocks[5, 5].Graphics[0].GraphicIndex = 10;
        map.Blocks[5, 5].NpcIndex = 1;
        var overlays = MapOverlayGeometry.Build(map, new WorldRenderOptions { ShowNpcs = false });
        Assert.Empty(overlays);
    }
}
