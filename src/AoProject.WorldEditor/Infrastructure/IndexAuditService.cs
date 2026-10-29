using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

internal sealed record IndexAuditIssue(string File, string Code, AuditSeverity Severity, string Message);

internal sealed record IndexAuditResult(
    int Files,
    int Errors,
    int Warnings,
    int Infos,
    IReadOnlyList<IndexAuditIssue> Issues,
    IReadOnlyDictionary<string, string> Stats);

/// <summary>
/// Structural audit of the index files the editor reads: declared counts vs
/// real sections, duplicates, gaps, out-of-range references and the mismatch
/// between the binary and text graphic catalogs. Read-only.
/// </summary>
internal sealed class IndexAuditService
{
    private const int MaxSections = 2_000_000;

    public IndexAuditResult Audit(string resourceRoot, string? mapsDirectory = null,
        WorldEditorResourceCatalog? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        var issues = new List<IndexAuditIssue>();
        var stats = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = 0;

        var graphicsInd = Find(resourceRoot, "graficos.ind");
        var graphicsIni = Find(resourceRoot, "graficos.ini");
        var minimap = Find(resourceRoot, "minimap.bin");

        var binary = graphicsInd is null ? null : AuditGraphicsBinary(graphicsInd, issues, stats);
        if (graphicsInd is not null) files++;
        if (graphicsIni is not null) { files++; AuditGraphicsText(graphicsIni, binary, issues, stats); }
        if (minimap is not null) { files++; AuditMinimapBin(minimap, binary, issues, stats); }

        var objects = Find(resourceRoot, "obj.dat");
        if (objects is not null)
        {
            files++;
            CheckIni(objects, "OBJ", "NumOBJs", issues, stats,
                required: ["GrhIndex"], allowZero: false);
        }

        var npcs = Find(resourceRoot, "npcs.dat");
        if (npcs is not null)
        {
            files++;
            CheckIni(npcs, "NPC", "NumNPCs", issues, stats,
                required: ["Name", "Body"], allowZero: true);
        }

        var bodies = Find(resourceRoot, "cuerpos.dat");
        if (bodies is not null)
        {
            files++;
            CheckIni(bodies, "BODY", "NumBodies", issues, stats,
                required: ["HeadOffsetX", "HeadOffsetY"], allowZero: true);
        }

        var helmets = Find(resourceRoot, "cascos.ini");
        if (helmets is not null)
        {
            files++;
            CheckIni(helmets, "HEAD", "NumHeads", issues, stats, required: ["Head1"], allowZero: false);
        }

        var weapons = Find(resourceRoot, "armas.dat");
        if (weapons is not null) { files++; CheckIni(weapons, "ARMA", "NumArmas", issues, stats, required: [], allowZero: true); }
        var shields = Find(resourceRoot, "escudos.dat");
        if (shields is not null) { files++; CheckIni(shields, "ESC", "NumEscudos", issues, stats, required: [], allowZero: true); }

        var triggers = Find(resourceRoot, "triggers.ini");
        if (triggers is not null)
        {
            files++;
            CheckIni(triggers, "Trig", "NumTriggers", issues, stats, required: ["Name"], allowZero: true);
        }

        var particles = Find(resourceRoot, "particles.ind");
        if (particles is not null) { files++; AuditParticles(particles, issues, stats); }

        var worldData = Find(resourceRoot, "mapsworlddata.dat");
        if (worldData is not null) { files++; AuditWorldData(worldData, mapsDirectory, issues, stats); }

        if (catalog is not null) CheckCatalogReferences(catalog, issues);

        var errors = issues.Count(issue => issue.Severity == AuditSeverity.Error);
        var warnings = issues.Count(issue => issue.Severity == AuditSeverity.Warning);
        var infos = issues.Count(issue => issue.Severity == AuditSeverity.Info);
        return new IndexAuditResult(files, errors, warnings, infos, issues, stats);
    }

    private sealed record GraphicsBinary(int Version, int DeclaredMax, int Records, int Active, HashSet<int> Ids);

    private static GraphicsBinary? AuditGraphicsBinary(string path, List<IndexAuditIssue> issues,
        Dictionary<string, string> stats)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: false);
            var version = reader.ReadInt32();
            var declared = reader.ReadInt32();
            if (declared is <= 0 or > MaxSections)
            {
                issues.Add(Error("graficos.ind", "IDX-HEADER", $"MaxGrh invalido: {declared}."));
                return null;
            }
            var ids = new HashSet<int>();
            var records = 0;
            while (stream.Position < stream.Length && !ids.Contains(declared))
            {
                var index = reader.ReadInt32();
                if (index is < 1 || index > declared)
                    issues.Add(Warning("graficos.ind", "IDX-RANGE", $"Indice GRH fuera de 1..{declared}: {index}."));
                if (!ids.Add(index))
                    issues.Add(Warning("graficos.ind", "IDX-DUP", $"Indice GRH duplicado: {index}."));
                var frames = reader.ReadInt16();
                if (frames <= 0)
                {
                    issues.Add(Error("graficos.ind", "IDX-FRAMES", $"GRH {index} con {frames} frames."));
                    break;
                }
                if (frames > 1)
                {
                    for (var frame = 0; frame < frames; frame++) reader.ReadInt32();
                    reader.ReadSingle();
                }
                else
                {
                    reader.ReadInt32();
                    reader.ReadInt16(); reader.ReadInt16(); reader.ReadInt16(); reader.ReadInt16();
                }
                records++;
                if (records > MaxSections) break;
            }
            var trailing = stream.Length - stream.Position;
            if (trailing != 0)
                issues.Add(Info("graficos.ind", "IDX-TRAILING", $"Quedan {trailing} bytes sin usar."));
            if (!ids.Contains(declared))
                issues.Add(Error("graficos.ind", "IDX-TRUNCATED",
                    $"El indice termina antes del GRH {declared} (registros={records})."));
            stats["graficos.ind.version"] = version.ToString();
            stats["graficos.ind.declared"] = declared.ToString();
            stats["graficos.ind.records"] = records.ToString();
            return new GraphicsBinary(version, declared, records, records, ids);
        }
        catch (Exception error) when (error is IOException or EndOfStreamException)
        {
            issues.Add(Error("graficos.ind", "IDX-PARSE", $"No se pudo leer: {error.Message}."));
            return null;
        }
    }

    private static void AuditGraphicsText(string path, GraphicsBinary? binary, List<IndexAuditIssue> issues,
        Dictionary<string, string> stats)
    {
        var document = LoadStrict(path, issues);
        if (document is null) return;
        var declared = document.GetInt("INIT", "NumGrh");
        stats["graficos.ini.NumGrh"] = declared.ToString();
        var entries = new Dictionary<int, string>();
        var duplicate = 0;
        foreach (var raw in File.ReadLines(path, Encoding.Latin1))
        {
            var line = raw.Trim();
            if (!line.StartsWith("Grh", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = line.IndexOf('=');
            if (separator <= 3 || !int.TryParse(line[3..separator], out var index) || index <= 0) continue;
            if (!entries.TryAdd(index, line[(separator + 1)..])) duplicate++;
        }
        stats["graficos.ini.entries"] = entries.Count.ToString();
        if (entries.Count == 0)
            issues.Add(Error("graficos.ini", "IDX-EMPTY", "No hay entradas GrhN."));
        if (duplicate > 0)
            issues.Add(Warning("graficos.ini", "IDX-DUP", $"{duplicate} claves GrhN duplicadas."));
        if (declared > 0 && entries.Count > declared)
            issues.Add(Warning("graficos.ini", "IDX-COUNT",
                $"Hay {entries.Count} entradas y NumGrh={declared}."));
        else if (declared > entries.Count)
            issues.Add(Info("graficos.ini", "IDX-COUNT",
                $"NumGrh={declared} es el maximo; {declared - entries.Count} GRH sin definir en el texto."));
        if (binary is not null)
        {
            var missing = entries.Keys.Count(index => index > binary.DeclaredMax);
            stats["graficos.ini.beyondBinary"] = missing.ToString();
            if (missing > 0)
                issues.Add(Info("graficos.ini", "IDX-GRAPHICS",
                    $"{missing} GRH del catalogo de texto no estan en graficos.ind (max {binary.DeclaredMax})."));
            var absent = entries.Keys.Count(index => !binary.Ids.Contains(index));
            if (absent > missing)
                issues.Add(Warning("graficos.ini", "IDX-GRAPHICS",
                    $"{absent - missing} GRH dentro del rango binario no tienen registro en graficos.ind."));
        }
    }

    private static void AuditMinimapBin(string path, GraphicsBinary? binary, List<IndexAuditIssue> issues,
        Dictionary<string, string> stats)
    {
        var entries = new FileInfo(path).Length / sizeof(int);
        stats["minimap.bin.entries"] = entries.ToString();
        if (binary is null) return;
        var shortfall = binary.Active - entries;
        stats["minimap.bin.shortfall"] = shortfall.ToString();
        if (shortfall > 0)
            issues.Add(Warning("minimap.bin", "IDX-MINIMAP",
                $"Faltan {shortfall} colores: {binary.Active} GRH activos vs {entries} entradas."));
        else if (shortfall < 0)
            issues.Add(Info("minimap.bin", "IDX-MINIMAP",
                $"Sobran {-shortfall} entradas respecto de {binary.Active} GRH activos."));
    }

    private static void CheckIni(string path, string prefix, string countKey, List<IndexAuditIssue> issues,
        Dictionary<string, string> stats, IReadOnlyCollection<string> required, bool allowZero)
    {
        var name = Path.GetFileName(path);
        var document = LoadStrict(path, issues);
        if (document is null) return;
        var declared = document.GetInt("INIT", countKey);
        stats[$"{name}.{countKey}"] = declared.ToString();
        if (declared <= 0 || declared > MaxSections)
        {
            issues.Add(Error(name, "IDX-HEADER", $"{countKey} invalido: {declared}."));
            return;
        }

        var sections = new HashSet<int>();
        var duplicates = 0;
        foreach (var raw in File.ReadLines(path, Encoding.Latin1))
        {
            var line = raw.Trim();
            if (!line.StartsWith('[') || !line.EndsWith(']')) continue;
            var label = line[1..^1].Trim();
            if (!label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(label[prefix.Length..], out var index)) continue;
            if (!sections.Add(index)) duplicates++;
        }
        stats[$"{name}.sections"] = sections.Count.ToString();
        if (duplicates > 0)
            issues.Add(Warning(name, "IDX-DUP", $"{duplicates} secciones {prefix}<n> duplicadas."));
        if (sections.Count != declared)
        {
            var missing = Enumerable.Range(1, declared).Count(index => !sections.Contains(index));
            issues.Add(Warning(name, "IDX-COUNT",
                $"{countKey}={declared} pero hay {sections.Count} secciones ({missing} faltantes)."));
        }
        var extra = sections.Count(index => index < 1 || index > declared);
        if (extra > 0)
            issues.Add(Info(name, "IDX-COUNT", $"{extra} secciones fuera de 1..{declared}."));

        if (required.Count == 0) return;
        var empty = 0;
        foreach (var index in sections.Where(index => index >= 1 && index <= declared))
        {
            var section = prefix + index;
            foreach (var field in required)
            {
                var value = document.Get(section, field);
                if (value.Length > 0 && (allowZero || value != "0")) continue;
                empty++;
                break;
            }
        }
        if (empty > 0)
            issues.Add(Warning(name, "IDX-EMPTY",
                $"{empty} secciones sin {string.Join('/', required)}."));
    }

    private static void AuditParticles(string path, List<IndexAuditIssue> issues, Dictionary<string, string> stats)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: false);
            reader.ReadInt32();
            var declared = reader.ReadInt16();
            stats["particles.ind.count"] = declared.ToString();
            if (declared <= 0) issues.Add(Error("particles.ind", "IDX-HEADER", $"Cantidad invalida: {declared}."));
        }
        catch (Exception error) when (error is IOException or EndOfStreamException)
        {
            issues.Add(Error("particles.ind", "IDX-PARSE", $"No se pudo leer: {error.Message}."));
        }
    }

    private static void AuditWorldData(string path, string? mapsDirectory, List<IndexAuditIssue> issues,
        Dictionary<string, string> stats)
    {
        var document = LoadStrict(path, issues);
        if (document is null) return;
        var total = document.GetInt("INIT", "TOTALWORLDS");
        stats["mapsworlddata.TOTALWORLDS"] = total.ToString();
        var available = mapsDirectory is null || !Directory.Exists(mapsDirectory)
            ? null
            : Directory.EnumerateFiles(mapsDirectory, "*.csm")
                .Select(file => new string(Path.GetFileNameWithoutExtension(file).Where(char.IsDigit).ToArray()))
                .Where(digits => short.TryParse(digits, out _))
                .Select(short.Parse).ToHashSet();
        var worlds = document.Sections
            .Where(section => section.StartsWith("WORLDMAP", StringComparison.OrdinalIgnoreCase))
            .Select(section => (Section: section, Id: int.TryParse(section[8..], out var id) ? id : 0))
            .Where(item => item.Id > 0)
            .OrderBy(item => item.Id)
            .ToArray();
        stats["mapsworlddata.worlds"] = worlds.Length.ToString();
        if (total > 0 && worlds.Length != total)
            issues.Add(Warning("mapsworlddata.dat", "IDX-WORLD",
                $"TOTALWORLDS={total} pero hay {worlds.Length} secciones WORLDMAPn."));

        foreach (var (section, id) in worlds)
        {
            var width = document.GetInt(section, "ANCHO");
            var height = document.GetInt(section, "ALTO");
            if (width <= 0 || height <= 0)
            {
                issues.Add(Error("mapsworlddata.dat", "IDX-WORLD", $"{section} sin ANCHO/ALTO validos."));
                continue;
            }
            var cells = width * height;
            var positions = new HashSet<int>();
            var duplicates = 0;
            var unknown = new SortedSet<short>();
            var empty = 0;
            for (var index = 1; index <= cells; index++)
            {
                var value = document.Get(section, index.ToString());
                if (value.Length == 0)
                {
                    empty++;
                    continue;
                }
                if (!positions.Add(index)) duplicates++;
                if (!short.TryParse(value, out var mapId))
                {
                    issues.Add(Warning("mapsworlddata.dat", "IDX-WORLD", $"{section}[{index}] no es un mapa: '{value}'."));
                    continue;
                }
                if (mapId <= 0)
                {
                    empty++;
                    continue;
                }
                if (available is not null && !available.Contains(mapId)) unknown.Add(mapId);
            }
            var extra = CountExtraKeys(path, section, cells);
            stats[$"mapsworlddata.world{id}"] = $"{width}x{height} celdas={cells} vacias={empty}";
            if (duplicates > 0)
                issues.Add(Warning("mapsworlddata.dat", "IDX-WORLD", $"{section}: {duplicates} indices duplicados."));
            if (extra > 0)
                issues.Add(Info("mapsworlddata.dat", "IDX-WORLD", $"{section}: {extra} claves fuera de 1..{cells}."));
            if (unknown.Count > 0)
                issues.Add(Warning("mapsworlddata.dat", "IDX-WORLD",
                    $"{section}: mapas sin CSM en la carpeta [{string.Join(",", unknown.Take(12))}]" +
                    (unknown.Count > 12 ? ",..." : string.Empty)));
        }
    }

    private static int CountExtraKeys(string path, string section, int cells)
    {
        var extra = 0;
        var inside = false;
        foreach (var raw in File.ReadLines(path, Encoding.Latin1))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inside = line[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inside) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            if (!int.TryParse(line[..separator].Trim(), out var index) || index < 1 || index > cells) extra++;
        }
        return extra;
    }

    private static void CheckCatalogReferences(WorldEditorResourceCatalog catalog, List<IndexAuditIssue> issues)
    {
        var graphics = catalog.Graphics.Count;
        var objectsWithoutGraphic = catalog.Objects
            .Select((item, index) => (item, index))
            .Where(pair => pair.index > 0 && (pair.item.GraphicIndex <= 0 || pair.item.GraphicIndex >= graphics))
            .Select(pair => pair.index).ToArray();
        if (objectsWithoutGraphic.Length > 0)
            issues.Add(Warning("obj.dat", "IDX-GRH",
                $"{objectsWithoutGraphic.Length} objetos sin GRH valido [{string.Join(",", objectsWithoutGraphic.Take(12))}]."));

        var npcOutOfRange = new List<int>();
        for (var index = 1; index < catalog.Npcs.Count; index++)
        {
            var npc = catalog.Npcs[index];
            if (npc.Body <= 0 || npc.Body >= catalog.Bodies.Length || npc.Head < 0 ||
                npc.Head >= catalog.Heads.Length || npc.Weapon < 0 || npc.Weapon >= catalog.Weapons.Length ||
                npc.Shield < 0 || npc.Shield >= catalog.Shields.Length ||
                npc.Helmet < 0 || npc.Helmet >= catalog.Helmets.Length)
                npcOutOfRange.Add(index);
        }
        if (npcOutOfRange.Count > 0)
            issues.Add(Warning("npcs.dat", "IDX-RANGE",
                $"{npcOutOfRange.Count} NPC con body/head/equipo fuera de rango [{string.Join(",", npcOutOfRange.Take(12))}]."));
    }

    private static IniDocument? LoadStrict(string path, List<IndexAuditIssue> issues)
    {
        try { return IniDocument.Load(path, Encoding.Latin1); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            issues.Add(Error(Path.GetFileName(path), "IDX-PARSE", $"No se pudo leer: {error.Message}."));
            return null;
        }
    }

    private static string? Find(string root, string fileName) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName,
                    StringComparison.OrdinalIgnoreCase))
            : null;

    private static IndexAuditIssue Error(string file, string code, string message) =>
        new(file, code, AuditSeverity.Error, message);

    private static IndexAuditIssue Warning(string file, string code, string message) =>
        new(file, code, AuditSeverity.Warning, message);

    private static IndexAuditIssue Info(string file, string code, string message) =>
        new(file, code, AuditSeverity.Info, message);
}
