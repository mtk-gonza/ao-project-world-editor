using System.Text;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace AoProject.WorldEditor.Tests;

[Trait("Category", "Audit")]
public sealed class FullAuditTests
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("AO_WORLD_AUDIT") == "1";
    private readonly ITestOutputHelper _output;
    public FullAuditTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Full_audit_writes_the_reports()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var maps = Path.Combine(root, "Mapas");
        if (!Directory.Exists(maps)) return;
        var result = new WorldAuditService().Run(maps, root);
        _output.WriteLine($"audit={result.Directory} maps={result.Maps} mapE={result.MapErrors} " +
            $"mapW={result.MapWarnings} worlds={result.Worlds} worldE={result.WorldErrors}");
        Assert.True(result.Maps > 0);
        Assert.Equal(3, result.Worlds);
        Assert.True(File.Exists(Path.Combine(result.Directory, "map-data-issues.tsv")));
        Assert.True(File.Exists(Path.Combine(result.Directory, "world-coordinate-issues.tsv")));
    }

    [Fact]
    public void Full_outputs_are_generated_into_artifacts()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var maps = Path.Combine(root, "Mapas");
        if (!Directory.Exists(maps)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var result = new WorldOutputService().Generate(maps, catalog, referenceRoot: root);
        _output.WriteLine($"outputs={result.Directory} maps={result.Maps} minimaps={result.Minimaps} " +
            $"bmp={result.ClientMinimaps} worlds={result.WorldMinimaps} errors={result.Errors}");
        Assert.True(result.Maps > 0);
        Assert.True(File.Exists(Path.Combine(result.Directory, "NameMapa.dat")));
        Assert.True(File.Exists(Path.Combine(result.Directory, "translados.ini")));
        Assert.Equal(result.Maps, result.ClientMinimaps);
        Assert.Equal(3, result.WorldMinimaps);
        foreach (var worldId in new[] { 1, 2, 3 })
        {
            var full = Path.Combine(result.Directory, "worldmap", $"worldmap{worldId}.png");
            var preview = Path.Combine(result.Directory, "worldmap", $"worldmap{worldId}-preview.png");
            Assert.True(File.Exists(full), $"Falta {full}");
            Assert.True(File.Exists(preview), $"Falta {preview}");
            using var image = new System.Drawing.Bitmap(full);
            Assert.Equal(1900, image.Width);
            Assert.Equal(2200, image.Height);
            using var small = new System.Drawing.Bitmap(preview);
            Assert.Equal(475, small.Width);
            Assert.Equal(550, small.Height);
        }
        var issues = new WorldOutputAuditService().Audit(maps, result.Directory, true, root);
        var report = new WorldOutputAuditService().WriteReport(issues, result.Directory);
        _output.WriteLine($"output audit issues={issues.Count} errors={issues.Count(issue => issue.Severity == Domain.AuditSeverity.Error)} report={report}");
        foreach (var issue in issues.Take(10))
            _output.WriteLine($"  {issue.Severity} {issue.Code} map{issue.MapId}: {issue.Message}");
        Assert.True(File.Exists(report));
        Assert.DoesNotContain(issues, issue => issue.Severity == Domain.AuditSeverity.Error);

        // The legacy descriptions are not in the CSM; they must survive because
        // the generator seeds from namemapa.dat instead of rewriting it.
        var reference = Path.Combine(root, "init", "namemapa.dat");
        if (File.Exists(reference))
        {
            static int Descriptions(string path) => File.ReadAllLines(path, Encoding.Latin1)
                .Count(line => System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^mapa\d+Desc="));
            var expected = Descriptions(reference);
            var actual = Descriptions(Path.Combine(result.Directory, "NameMapa.dat"));
            _output.WriteLine($"descripciones legacy: referencia={expected} generado={actual}");
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// Compares the generated ancillary files against the shipped legacy ones.
    /// The reference files predate some CSM edits, so a small drift is expected;
    /// the thresholds only catch format or index regressions.
    /// </summary>
    [Fact]
    public void Legacy_reference_outputs_use_the_same_format_and_data()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var maps = Path.Combine(root, "Mapas");
        var transfersPath = Path.Combine(root, "MiniMap", "translados.ini");
        var namesPath = Path.Combine(root, "init", "namemapa.dat");
        if (!Directory.Exists(maps) || !File.Exists(transfersPath) || !File.Exists(namesPath)) return;

        var transfers = IniDocument.Load(transfersPath, Encoding.Latin1);
        var names = IniDocument.Load(namesPath, Encoding.Latin1);
        var service = new CsmMapFileService();
        int sections = 0, completeSections = 0, keys = 0, keysMatched = 0;
        int nameTotal = 0, nameMatched = 0;
        foreach (var path in Directory.EnumerateFiles(maps, "*.csm"))
        {
            var digits = new string(Path.GetFileNameWithoutExtension(path).Where(char.IsDigit).ToArray());
            if (!short.TryParse(digits, out var id) || id <= 0 || new FileInfo(path).Length == 0) continue;
            var map = service.Load(path);
            var section = $"mapa{id}";
            if (transfers.HasSection(section))
            {
                sections++;
                var expected = new (string Key, short Value)[]
                {
                    ("Abajo", map.Blocks[50, 91].TileExit.Map),
                    ("Arriba", map.Blocks[50, 10].TileExit.Map),
                    ("Izquierda", map.Blocks[13, 50].TileExit.Map),
                    ("Derecha", map.Blocks[88, 50].TileExit.Map)
                };
                var all = true;
                foreach (var (key, value) in expected)
                {
                    keys++;
                    if (short.TryParse(transfers.Get(section, key), out var actual) && actual == value) keysMatched++;
                    else all = false;
                }
                if (all) completeSections++;
            }

            var referenceName = names.Get("NameMapa", $"mapa{id}");
            if (referenceName.Length == 0) continue;
            nameTotal++;
            if (string.Equals(referenceName.Trim(), (map.Info.Name ?? string.Empty).Trim(), StringComparison.Ordinal))
                nameMatched++;
        }

        var keyRatio = keys == 0 ? 0 : (double)keysMatched / keys;
        var nameRatio = nameTotal == 0 ? 0 : (double)nameMatched / nameTotal;
        _output.WriteLine($"translados: secciones={sections} completas={completeSections} " +
            $"keys={keysMatched}/{keys} ({keyRatio:P1}) | namemapa: {nameMatched}/{nameTotal} ({nameRatio:P1})");
        Assert.True(sections > 800, $"Se esperaban ~839 secciones legacy, hay {sections}.");
        Assert.True(keyRatio >= 0.95, $"Coincidencia de translados {keyRatio:P1} por debajo del 95%.");
        Assert.True(nameRatio >= 0.95, $"Coincidencia de NameMapa {nameRatio:P1} por debajo del 95%.");
    }

    /// <summary>
    /// Finds an NPC with equipment and a valid body, then checks that the
    /// character renderer resolves the equipment GRHs. Uses real resource data.
    /// </summary>
    [Fact]
    public void Npc_equipment_resolves_into_character_sprites()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var maps = Path.Combine(root, "Mapas");
        if (!Directory.Exists(maps)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        foreach (var path in Directory.EnumerateFiles(maps, "*.csm").OrderBy(item => item).Take(40))
        {
            var map = new CsmMapFileService().Load(path);
            var characters = new CharacterListService();
            characters.RefreshAll(map, catalog.Npcs);
            foreach (var character in characters.Characters)
            {
                if (character.Weapon == 0 && character.Shield == 0 && character.Helmet == 0) continue;
                if (character.Body <= 0 || character.Body >= catalog.Bodies.Length) continue;
                var heading = character.Heading;
                if (catalog.Bodies[character.Body].Walk[heading].GraphicIndex <= 0) continue;
                var expected = EquipmentGraphics(catalog, character);
                if (expected.Count == 0) continue;

                var without = new CharacterRenderService().Render(
                    new[] { character }, catalog.Bodies, catalog.Heads);
                var with = new CharacterRenderService().Render(
                    new[] { character }, catalog.Bodies, catalog.Heads,
                    catalog.Helmets, catalog.Weapons, catalog.Shields);
                _output.WriteLine($"{Path.GetFileName(path)} npc@{character.X},{character.Y} " +
                    $"equipo={string.Join(",", expected)} sin={without.Count} con={with.Count}");
                foreach (var graphic in expected)
                {
                    Assert.True(graphic < catalog.Graphics.Count, $"GRH {graphic} fuera del catalogo.");
                    Assert.True(catalog.Graphics[graphic].Active, $"GRH {graphic} inactivo.");
                }
                Assert.DoesNotContain(without, sprite => expected.Contains(sprite.GraphicIndex));
                foreach (var graphic in expected)
                    Assert.Contains(with, sprite => sprite.GraphicIndex == graphic);
                return;
            }
        }
        _output.WriteLine("No se encontro un NPC con cuerpo y equipo validos en los primeros 40 mapas.");
    }

    private static List<int> EquipmentGraphics(WorldEditorResourceCatalog catalog, CharacterEntry character)
    {
        var graphics = new List<int>();
        var heading = character.Heading;
        if (character.Helmet > 0 && character.Helmet < catalog.Helmets.Length)
            graphics.Add(catalog.Helmets[character.Helmet].Walk[heading].GraphicIndex);
        if (character.Weapon > 0 && character.Weapon < catalog.Weapons.Length)
            graphics.Add(catalog.Weapons[character.Weapon].Walk[heading].GraphicIndex);
        if (character.Shield > 0 && character.Shield < catalog.Shields.Length)
            graphics.Add(catalog.Shields[character.Shield].Walk[heading].GraphicIndex);
        return graphics.Where(graphic => graphic > 0).Distinct().ToList();
    }

    /// <summary>
    /// Data check: for a real map with a strongly coloured round light, the
    /// dominant channel of the stored colour must stay dominant around it.
    /// </summary>
    [Fact]
    public void Real_round_lights_keep_their_channel_order()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var maps = Path.Combine(root, "Mapas");
        if (!Directory.Exists(maps)) return;
        foreach (var path in Directory.EnumerateFiles(maps, "*.csm")
                     .OrderBy(item => Path.GetFileName(item), StringComparer.OrdinalIgnoreCase))
        {
            var map = new CsmMapFileService().Load(path);
            for (var x = 1; x <= WorldMap.Width; x++)
            for (var y = 1; y <= WorldMap.Height; y++)
            {
                var light = map.Blocks[x, y].Light;
                if (light.Range < 100) continue;
                var red = (light.Color >> 16) & 0xFF;
                var green = (light.Color >> 8) & 0xFF;
                var blue = light.Color & 0xFF;
                var max = Math.Max(red, Math.Max(green, blue));
                var min = Math.Min(red, Math.Min(green, blue));
                if (max < 0x60 || max - min < 0x40) continue;

                var lights = new MapLightingService().Compute(map, MapLightingService.DayLight);
                var color = lights[x, y, 0];
                var outRed = (color >> 16) & 0xFF;
                var outGreen = (color >> 8) & 0xFF;
                var outBlue = color & 0xFF;
                var expected = max == red ? "red" : max == green ? "green" : "blue";
                var actual = outRed >= outGreen && outRed >= outBlue ? "red"
                    : outGreen >= outBlue ? "green" : "blue";
                _output.WriteLine($"{Path.GetFileName(path)} ({x},{y}) color=0x{light.Color:X6} " +
                    $"esperado={expected} obtenido={actual} rgb={outRed},{outGreen},{outBlue}");
                Assert.Equal(expected, actual);
                return;
            }
        }
        _output.WriteLine("No se encontro una luz redonda con color dominante.");
    }

    /// <summary>Structural audit of the index files on the real resources.</summary>
    [Fact]
    public void Index_audit_runs_on_the_real_resources()
    {
        if (!Enabled) return;
        var root = ResourceRoot();
        var maps = Path.Combine(root, "Mapas");
        if (!Directory.Exists(maps)) return;
        var catalog = WorldEditorResourceCatalog.Load(root);
        var result = new IndexAuditService().Audit(root, maps, catalog);
        _output.WriteLine($"files={result.Files} E={result.Errors} W={result.Warnings} I={result.Infos}");
        foreach (var issue in result.Issues)
            _output.WriteLine($"  {issue.Severity} {issue.File} {issue.Code}: {issue.Message}");
        foreach (var pair in result.Stats.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            _output.WriteLine($"  stat {pair.Key}={pair.Value}");
        Assert.Equal(0, result.Errors);
        Assert.Contains("graficos.ind.records", result.Stats.Keys);
        Assert.Contains("minimap.bin.entries", result.Stats.Keys);
        Assert.Contains("mapsworlddata.TOTALWORLDS", result.Stats.Keys);
    }

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
}
