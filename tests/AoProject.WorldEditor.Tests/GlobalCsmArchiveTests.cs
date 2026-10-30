using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalCsmArchiveTests
{
    [Fact]
    public void Archive_keeps_map_ids_csm_payloads_and_sidecars()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-global-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "mapa1.csm");
            var second = Path.Combine(root, "mapa2.csm");
            new SectorCsmFileService().Save(new WorldSector(), first);
            new SectorCsmFileService().Save(new WorldSector(), second);
            File.WriteAllText(Path.Combine(root, "Mapa1.dat"), "NAME=Primer mapa\r\nPK=1\r\n");
            File.WriteAllBytes(Path.Combine(root, "Mapa1.inf"), [1, 2, 3, 4]);

            var document = new GlobalWorldDocument(new GlobalWorldAssembly
            {
                Width = 2,
                Height = 1,
                Placements = [new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "world")]
            },
                new Dictionary<short, string> { [1] = first, [2] = second }, 1,
                (_, path) => new SectorCsmFileService().Load(path));
            var archive = Path.Combine(root, "mapaGlobal.csm");
            new GlobalCsmArchiveService().Save(document, archive);

            Assert.True(File.Exists(Path.ChangeExtension(archive, ".dat")));
            Assert.True(File.Exists(Path.ChangeExtension(archive, ".inf")));
            var globalDat = File.ReadAllText(Path.ChangeExtension(archive, ".dat"));
            Assert.Contains("[AssemblyPlacements]", globalDat);
            Assert.Contains("Mapa1=0,0,world", globalDat);
            Assert.Contains("[Mapa1]", globalDat);
            Assert.Equal(new short[] { 1, 2 }, GlobalCsmArchiveService.ReadSectorIds(archive));
            Assert.True(GlobalCsmArchiveService.TryRead(archive, 2, out var payload));
            Assert.Equal(File.ReadAllBytes(second), payload);
            Assert.True(GlobalCsmArchiveService.TryReadSidecar(archive, 1, "Mapa1.dat", out var dat));
            Assert.Contains("Primer mapa", System.Text.Encoding.Default.GetString(dat));
            Assert.True(GlobalCsmArchiveService.TryReadSidecar(archive, 1, "Mapa1.inf", out var inf));
            Assert.Equal([1, 2, 3, 4], inf);
            var audit = new GlobalArchiveAuditService().Audit(archive, document);
            Assert.Empty(audit.Issues.Where(issue => issue.Error));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void World_output_can_store_csm_and_metadata_in_resource_subfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-world-output-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "world"));
        Directory.CreateDirectory(Path.Combine(root, "Dat"));
        try
        {
            var source = Path.Combine(root, "mapa1.csm");
            new SectorCsmFileService().Save(new WorldSector(), source);
            var document = new GlobalWorldDocument(
                new GlobalWorldAssembly
                {
                    Width = 1,
                    Height = 1,
                    Placements = [new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "world")]
                },
                new Dictionary<short, WorldSector> { [1] = new SectorCsmFileService().Load(source) },
                new Dictionary<short, string> { [1] = source }, 1);
            var csm = Path.Combine(root, "world", "world.csm");
            var mapFile = Path.Combine(root, "world", "world.map");
            var dat = Path.Combine(root, "Dat", "world.dat");
            var inf = Path.Combine(root, "Dat", "world.inf");

            new GlobalCsmArchiveService().Save(document, csm, dat, inf, mapFile);

            Assert.True(File.Exists(csm));
            Assert.True(File.Exists(mapFile));
            Assert.True(File.Exists(dat));
            Assert.True(File.Exists(inf));
            var audit = new GlobalArchiveAuditService().Audit(csm, document, dat, inf, mapFile);
            Assert.Empty(audit.Issues.Where(issue => issue.Error));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Global_map_snapshot_embeds_the_generated_world_image()
    {
        var document = new GlobalWorldDocument(
            new GlobalWorldAssembly
            {
                Width = 1,
                Height = 1,
                Placements = [new GlobalSectorPlacement(1, new GlobalChunkCoordinate(0, 0), "world")]
            },
            new Dictionary<short, WorldSector>(), new Dictionary<short, string>(), 1);
        var image = new GlobalResolvedWorldSnapshot.EmbeddedImage("worldmap/world.png", [1, 2, 3]);

        var data = GlobalResolvedWorldSnapshot.Serialize(document, [image]);
        var snapshot = GlobalResolvedWorldSnapshot.Read(data);

        var embedded = Assert.Single(snapshot.Images);
        Assert.Equal("worldmap/world.png", embedded.Name);
        Assert.Equal([1, 2, 3], embedded.Data);
    }
}
