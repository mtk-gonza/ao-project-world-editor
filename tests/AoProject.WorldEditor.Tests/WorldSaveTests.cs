using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class WorldSaveTests
{
    [Fact]
    public void SaveWorld_writes_only_changed_maps_and_creates_a_backup()
    {
        using var world = new TempWorld();
        var session = world.Session;
        var global = session.LoadWorld(world.MapsDirectory);

        Assert.Equal(1, global.LoadedMapCount);
        Assert.NotNull(session.CurrentMap);
        session.CurrentMap!.Info.Name = "edited";
        session.CurrentMap.Info.Changed = true;

        var backupRoot = Path.Combine(world.Root, "backups");
        var result = session.SaveWorld(backupRoot);

        var saved = Assert.Single(result.Saved);
        Assert.Empty(result.Errors);
        Assert.EndsWith("mapa1.csm", saved);
        Assert.False(session.CurrentMap.Info.Changed);
        Assert.Equal(1, session.CurrentWorld!.LoadedMapCount);
        Assert.Equal("edited", new SectorCsmFileService().Load(saved).Info.Name);
        Assert.True(Directory.EnumerateFiles(backupRoot, "mapa1.csm", SearchOption.AllDirectories).Any());

        // map2 was never loaded, so it must not be touched or saved.
        var untouched = new SectorCsmFileService().Load(Path.Combine(world.MapsDirectory, "mapa2.csm"));
        Assert.Equal("two", untouched.Info.Name);
    }

    [Fact]
    public void Activating_the_same_map_keeps_the_undo_history()
    {
        using var world = new TempWorld();
        var session = world.Session;
        session.LoadWorld(world.MapsDirectory);
        session.SetGraphic(10, 10, 0, 5);
        Assert.True(session.CanUndo);

        Assert.True(session.ActivateWorldMap(1));
        Assert.True(session.CanUndo);

        // Switching to a different map clears the local full-map snapshots
        // (they have no map id) but keeps global per-map snapshots.
        Assert.True(session.ActivateWorldMap(2));
        Assert.False(session.CanUndo);
    }

    private sealed class TempWorld : IDisposable
    {
        public TempWorld()
        {
            Root = Path.Combine(Path.GetTempPath(), "ao-world-save-" + Guid.NewGuid().ToString("N"));
            MapsDirectory = Path.Combine(Root, "Mapas");
            var initDirectory = Path.Combine(Root, "init");
            Directory.CreateDirectory(MapsDirectory);
            Directory.CreateDirectory(initDirectory);
            var csm = new SectorCsmFileService();
            var first = new WorldSector();
            first.Info.Name = "one";
            var second = new WorldSector();
            second.Info.Name = "two";
            csm.Save(first, Path.Combine(MapsDirectory, "mapa1.csm"));
            csm.Save(second, Path.Combine(MapsDirectory, "mapa2.csm"));
            File.WriteAllText(Path.Combine(initDirectory, "mapsworlddata.dat"),
                "[INIT]\nTOTALWORLDS=1\n[WORLDMAP1]\n1=1\n2=2\nANCHO=2\nALTO=1\n");
            Session = new WorldEditorSession(Root);
        }

        public string Root { get; }
        public string MapsDirectory { get; }
        public WorldEditorSession Session { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
