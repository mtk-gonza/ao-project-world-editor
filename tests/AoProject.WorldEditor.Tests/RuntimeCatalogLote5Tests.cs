using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

/// <summary>
/// Lote 5: los catálogos no espaciales que todavía abría el cliente desde
/// <c>Dat/</c> (zonas, meditaciones y nombres de NPC) viajan en el mismo
/// <c>runtime-data/legacycatalogs.json</c> 1:1 que ya consume el servidor.
/// </summary>
public sealed class RuntimeCatalogLote5Tests
{
    [Fact]
    public void World_importer_publishes_the_client_legacy_catalogs()
    {
        Assert.Contains("zonas.dat", WorldEditorSession.LegacyCatalogFileNames);
        Assert.Contains("Meditaciones.dat", WorldEditorSession.LegacyCatalogFileNames);
        Assert.Contains("npcs.dat", WorldEditorSession.LegacyCatalogFileNames);
    }

    [Fact]
    public void Client_catalogs_preserve_sections_keys_and_raw_lines()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-clientcatalogs-");
        try
        {
            var bundle = Bundle(b => b.LegacyCatalogs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["zonas.dat"] = Encoding.UTF8.GetBytes("""
                    [Config]
                    Cantidad=1

                    [Zona7]
                    Mapa=254
                    Musica1=3
                    InviSinEfecto=1
                    """),
                ["Meditaciones.dat"] = Encoding.UTF8.GetBytes("""
                    [FXsMEDITATIONSBYLEVEL]
                    MeditationLevel1to12=153
                    """),
                ["npcs.dat"] = Encoding.UTF8.GetBytes("""
                    ' comentario
                    [INIT]
                    NumNPCs=2

                    [NPC1]
                    Name=Guardia
                    NpcType=2

                    [NPC2]
                    Name=Comerciante
                    """)
            });

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "legacycatalogs.json")));
            var sections = document.RootElement.GetProperty("Sections");
            Assert.Equal("254",
                sections.GetProperty("zonas.dat").GetProperty("Zona7").GetProperty("Mapa").GetString());
            Assert.Equal("153",
                sections.GetProperty("Meditaciones.dat").GetProperty("FXsMEDITATIONSBYLEVEL")
                    .GetProperty("MeditationLevel1to12").GetString());
            Assert.Equal("Comerciante",
                sections.GetProperty("npcs.dat").GetProperty("NPC2").GetProperty("Name").GetString());

            var lines = document.RootElement.GetProperty("Lines").GetProperty("npcs.dat")
                .EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
            Assert.Equal(new[]
            {
                "' comentario", "[INIT]", "NumNPCs=2", "", "[NPC1]", "Name=Guardia",
                "NpcType=2", "", "[NPC2]", "Name=Comerciante"
            }, lines);
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    private static UnifiedWorldBundle Bundle(Action<UnifiedWorldBundle> configure)
    {
        var bundle = new UnifiedWorldBundle
        {
            Events = "# events"u8.ToArray(),
            Treasures = "# treasures"u8.ToArray(),
            Quests = "# quests"u8.ToArray(),
            Npcs = "# npcs"u8.ToArray(),
            Objects = "# objects"u8.ToArray(),
            Castles = "[]"u8.ToArray(),
            Spells = "# spells"u8.ToArray(),
            EffectsOverTime = "# eot"u8.ToArray(),
            GlobalQuests = "# global quests"u8.ToArray(),
            Scenarios = new Dictionary<string, byte[]>()
        };
        configure(bundle);
        return bundle;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch (IOException) { }
    }
}