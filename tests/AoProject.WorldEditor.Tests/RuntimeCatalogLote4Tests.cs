using System.Text;
using System.Text.Json;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

/// <summary>
/// Lote 4: los .dat/.ini/.txt no espaciales se publican 1:1 en
/// <c>runtime-data/legacycatalogs.json</c> junto al WORLD.
/// </summary>
public sealed class RuntimeCatalogLote4Tests
{
    [Fact]
    public void Runtime_data_publishes_legacy_catalogs_with_sections_and_raw_lines()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-legacycatalogs-");
        try
        {
            var bundle = Bundle(b => b.LegacyCatalogs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Balance.dat"] = Encoding.UTF8.GetBytes("""
                    ' comentario
                    GlobalQuestGatheringThresholdNeeded=100 in quests.dat es una nota

                    [EXP]
                    1=100

                    [GUILD]
                    GuildCallCooldown=1200
                    """),
                ["Help.dat"] = Encoding.UTF8.GetBytes("[INIT]\nNumLines=1\n\n[Help]\nLine1=ayuda\n"),
                ["NombresInvalidos.txt"] = Encoding.UTF8.GetBytes("Admin\nGM\n")
            });

            new UnifiedWorldRuntimeDataService().Save(bundle, root.FullName);

            using var manifest = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "manifest.json")));
            Assert.True(manifest.RootElement.GetProperty("Files").TryGetProperty("legacycatalogs.json", out _));

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "legacycatalogs.json")));
            var element = document.RootElement;
            Assert.Equal(1, element.GetProperty("Schema").GetInt32());
            Assert.Equal("legacycatalogs", element.GetProperty("Catalog").GetString());

            var sections = element.GetProperty("Sections");
            var balance = sections.GetProperty("Balance.dat");
            // La clave previa a la primera seccion queda en la seccion raiz "".
            Assert.Equal("100 in quests.dat es una nota",
                balance.GetProperty("").GetProperty("GlobalQuestGatheringThresholdNeeded").GetString());
            Assert.Equal("100", balance.GetProperty("EXP").GetProperty("1").GetString());
            Assert.Equal("1200", balance.GetProperty("GUILD").GetProperty("GuildCallCooldown").GetString());

            // Help.dat conserva ademas sus lineas crudas.
            var lines = element.GetProperty("Lines");
            Assert.Equal(new[] { "Admin", "GM" }, ReadStrings(lines.GetProperty("NombresInvalidos.txt")));
            Assert.Equal(new[] { "[INIT]", "NumLines=1", "", "[Help]", "Line1=ayuda" },
                ReadStrings(lines.GetProperty("Help.dat")));
            // Los .txt no publican secciones.
            Assert.False(sections.TryGetProperty("NombresInvalidos.txt", out _));
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    [Fact]
    public void Runtime_data_emits_an_empty_legacy_catalog_when_none_is_provided()
    {
        var root = Directory.CreateTempSubdirectory("ao-runtime-legacycatalogs-empty-");
        try
        {
            new UnifiedWorldRuntimeDataService().Save(Bundle(_ => { }), root.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(root.FullName, "runtime-data", "legacycatalogs.json")));
            Assert.Equal("legacycatalogs", document.RootElement.GetProperty("Catalog").GetString());
            Assert.Empty(document.RootElement.GetProperty("Sections").EnumerateObject());
            Assert.Empty(document.RootElement.GetProperty("Lines").EnumerateObject());
        }
        finally
        {
            TryDelete(root.FullName);
        }
    }

    private static string[] ReadStrings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();

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