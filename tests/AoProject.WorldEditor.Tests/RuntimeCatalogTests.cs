using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class RuntimeCatalogTests
{
    [Fact]
    public void Runtime_data_converts_legacy_treasure_ini_to_versioned_json()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-catalog-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = Encoding.UTF8.GetBytes("""
                    [Tesoros]
                    CantidadMapas=1
                    Mapa1=16
                    TiposDeTesoros=1
                    Tesoro1=3140-2

                    [Regalos]
                    CantidadMapas=1
                    Mapa1=293
                    TiposDeRegalos=1
                    Regalo1=3744-3
                    """),
                Quests = "# quests"u8.ToArray(),
                Npcs = "# npcs"u8.ToArray(),
                Objects = "# objects"u8.ToArray(),
                Castles = "[]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            var path = Path.Combine(root.FullName, "runtime-data", "treasures.json");
            Assert.True(File.Exists(path));
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var rootElement = document.RootElement;
            Assert.Equal(1, rootElement.GetProperty("Schema").GetInt32());
            Assert.Equal("treasures", rootElement.GetProperty("Catalog").GetString());
            Assert.Equal(16, rootElement.GetProperty("TreasureSectors")[0].GetInt32());
            Assert.Equal(3140, rootElement.GetProperty("Treasures")[0].GetProperty("ObjectIndex").GetInt32());
            Assert.Equal(293, rootElement.GetProperty("GiftSectors")[0].GetInt32());
            Assert.Equal(3744, rootElement.GetProperty("Gifts")[0].GetProperty("ObjectIndex").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Runtime_data_writes_versioned_castle_catalog()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-castles-");
        try
        {
            var bundle = new UnifiedWorldBundle
            {
                Events = "# events"u8.ToArray(),
                Treasures = "# treasures"u8.ToArray(),
                Quests = "# quests"u8.ToArray(),
                Npcs = "# npcs"u8.ToArray(),
                Objects = "# objects"u8.ToArray(),
                Castles = "[{\"Id\":3,\"Name\":\"Castle\"}]"u8.ToArray()
            };

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "castles.json")));
            Assert.Equal("castles", document.RootElement.GetProperty("Catalog").GetString());
            Assert.Equal(3, document.RootElement.GetProperty("Items")[0].GetProperty("Id").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root.FullName, true); } catch (IOException) { }
        }
    }
}
