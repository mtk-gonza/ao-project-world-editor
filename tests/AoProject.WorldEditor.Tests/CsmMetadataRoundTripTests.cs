using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class CsmMetadataRoundTripTests
{
    [Fact]
    public void Save_writes_light_then_particle_counts_but_particle_then_light_payload()
    {
        var map = new WorldSector();
        map.Blocks[2, 3].Light.Range = 120;
        map.Blocks[2, 3].Light.Color = 0x112233;
        map.Blocks[4, 5].ParticleIndex = 7;
        var path = Path.Combine(Path.GetTempPath(), $"csm-order-{Guid.NewGuid():N}.csm");

        try
        {
            var service = new SectorCsmFileService();
            service.Save(map, path);
            var bytes = File.ReadAllBytes(path);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            for (var i = 0; i < 6; i++) _ = reader.ReadInt32();
            Assert.Equal(1, reader.ReadInt32()); // lights in the header
            Assert.Equal(1, reader.ReadInt32()); // particles in the header
            var loaded = service.Load(path);
            Assert.Equal(120, loaded.Blocks[2, 3].Light.Range);
            Assert.Equal(7, loaded.Blocks[4, 5].ParticleIndex);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Csm_roundtrip_preserves_reference_metadata_layout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ao-world-editor-{Guid.NewGuid():N}.csm");
        try
        {
            var source = new WorldSector
            {
                CsmSectorSize = new CsmSectorSize(99, 2, 98, 3)
            };
            source.Info.Name = "mapa";
            source.Info.Backup = 1;
            source.Info.Restriction = "restriccion";
            source.Info.MusicNumberHigh = 12;
            source.Info.MusicNumberLow = 34;
            source.Info.Music = "34";
            source.Info.Safety = 1;
            source.Info.Zone = "zona";
            source.Info.Terrain = "terreno";
            source.Info.Ambient = "ambiente";
            source.Environment.BaseLight = 0x10203040;
            source.Info.LetterGraphic = 123;
            source.Info.Level = 456;
            source.Info.Extra2 = 789;
            source.Info.Exit = "salida";
            source.Environment.Rain = 1;
            source.Environment.Snow = 2;
            source.Environment.Fog = 3;
            source.Blocks[7, 8].ParticleIndex = 11;
            source.Blocks[9, 10].Light.Range = 105;
            source.Blocks[9, 10].Light.Color = 0x000000FF;

            var service = new SectorCsmFileService();
            service.Save(source, path);
            var loaded = service.Load(path);

            Assert.Equal(source.CsmSectorSize, loaded.CsmSectorSize);
            Assert.Equal(source.Info.Name, loaded.Info.Name);
            Assert.Equal(source.Info.MusicNumberHigh, loaded.Info.MusicNumberHigh);
            Assert.Equal(source.Info.MusicNumberLow, loaded.Info.MusicNumberLow);
            Assert.Equal(source.Info.Safety, loaded.Info.Safety);
            Assert.Equal(source.Info.Ambient, loaded.Info.Ambient);
            Assert.Equal(source.Environment.BaseLight, loaded.Environment.BaseLight);
            Assert.Equal(source.Info.LetterGraphic, loaded.Info.LetterGraphic);
            Assert.Equal(source.Info.Level, loaded.Info.Level);
            Assert.Equal(source.Info.Extra2, loaded.Info.Extra2);
            Assert.Equal(source.Info.Exit, loaded.Info.Exit);
            Assert.Equal(source.Environment.Rain, loaded.Environment.Rain);
            Assert.Equal(source.Environment.Snow, loaded.Environment.Snow);
            Assert.Equal(source.Environment.Fog, loaded.Environment.Fog);
            Assert.Equal(11, loaded.Blocks[7, 8].ParticleIndex);
            Assert.Equal((short)105, loaded.Blocks[9, 10].Light.Range);
            Assert.Equal(0x000000FF, loaded.Blocks[9, 10].Light.Color);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Empty_legacy_map_size_falls_back_to_full_map()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ao-empty-size-{Guid.NewGuid():N}.csm");
        try
        {
            var map = new WorldSector { CsmSectorSize = new CsmSectorSize(0, 0, 0, 0) };
            new SectorCsmFileService().Save(map, path);

            var loaded = new SectorCsmFileService().Load(path);

            Assert.Equal(CsmSectorSize.Default, loaded.CsmSectorSize);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
