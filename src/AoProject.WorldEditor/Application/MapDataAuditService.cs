using System.Text;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Audits one CSM file: raw header/records, metadata, references against the
/// resource catalog, bounds, exits and a save/load round trip.
/// </summary>
internal sealed class MapDataAuditService
{
    private const int MaxRecords = 1_000_000;
    private const int MaxIssuesPerCode = 25;

    private readonly SectorCsmFileService _maps = new();
    private readonly WorldEditorResourceCatalog? _catalog;
    private readonly GraphicSourceResolver? _resolver;
    private readonly HashSet<int>? _triggerIndexes;

    public MapDataAuditService(WorldEditorResourceCatalog? catalog = null)
    {
        _catalog = catalog;
        if (catalog is not null)
        {
            try { _resolver = new GraphicSourceResolver(catalog.Root); } catch { }
            _triggerIndexes = catalog.Triggers.Select(trigger => trigger.Index).ToHashSet();
        }
    }

    public SectorAuditSummary Audit(
        short mapId,
        string path,
        IReadOnlyCollection<short>? availableMaps = null,
        bool roundTrip = true)
    {
        var issues = new Issues(mapId);
        var fileName = Path.GetFileName(path);
        var fileSize = 0;
        try { fileSize = (int)new FileInfo(path).Length; } catch { }

        RawAudit? raw = null;
        try { raw = ReadRaw(path, issues); }
        catch (Exception error)
        {
            issues.Add("CSM-READ", AuditSeverity.Error, $"No se pudo parsear el archivo: {error.Message}");
        }

        WorldSector? map = null;
        try { map = _maps.Load(path); }
        catch (Exception error)
        {
            issues.Add("CSM-READ", AuditSeverity.Error, $"No se pudo cargar el mapa: {error.Message}");
        }
        if (map is null)
        {
            issues.Flush();
            return new SectorAuditSummary(mapId, fileName, fileSize, string.Empty,
                issues.List.Count(issue => issue.Severity == AuditSeverity.Error),
                issues.List.Count(issue => issue.Severity == AuditSeverity.Warning),
                issues.List.Count(issue => issue.Severity == AuditSeverity.Info),
                1, 100, 1, 100, 0, 0, 0, 0, 0, 0, 0, issues.List, []);
        }

        if (raw is not null) CheckCounts(raw, issues);
        CheckMetadata(map, issues);
        CheckContentBounds(map, raw, issues);
        CheckReferences(map, issues);
        CheckExits(map, availableMaps, issues);
        if (roundTrip) CheckRoundTrip(map, issues);
        issues.Flush();

        var counts = CountPersisted(map);
        var size = raw is { SizeValid: true } ? raw.Size : map.CsmSectorSize;
        return new SectorAuditSummary(
            mapId, fileName, fileSize, map.Info.Name,
            issues.List.Count(issue => issue.Severity == AuditSeverity.Error),
            issues.List.Count(issue => issue.Severity == AuditSeverity.Warning),
            issues.List.Count(issue => issue.Severity == AuditSeverity.Info),
            size.XMin, size.XMax, size.YMin, size.YMax,
            counts.Blocked, counts.Triggers, counts.Lights, counts.Particles,
            counts.Npcs, counts.Objects, counts.Exits,
            issues.List, CollectBorderExits(map));
    }

    private static void CheckCounts(RawAudit raw, Issues issues)
    {
        for (var index = 0; index < raw.Counts.Length; index++)
        {
            if (raw.Counts[index] == raw.Unique[index]) continue;
            if (raw.Counts[index] < raw.Unique[index])
                issues.Add("CSM-COUNT", AuditSeverity.Error,
                    $"Count {index} = {raw.Counts[index]} pero hay {raw.Unique[index]} registros unicos.");
            else
                issues.Add("CSM-COUNT", AuditSeverity.Warning,
                    $"Count {index} = {raw.Counts[index]} pero solo {raw.Unique[index]} registros unicos (duplicados o fuera de 1..100).");
        }
    }

    private static void CheckMetadata(WorldSector map, Issues issues)
    {
        var info = map.Info;
        if (string.IsNullOrWhiteSpace(info.Name))
            issues.Add("CSM-META", AuditSeverity.Info, "El mapa no tiene nombre.");
        if (info.MusicNumberHigh < 0 || info.MusicNumberLow < 0)
            issues.Add("CSM-META", AuditSeverity.Warning, "Musica con ID negativo.");
        if (info.Safety > 1)
            issues.Add("CSM-META", AuditSeverity.Warning, $"Safety fuera de 0/1: {info.Safety}.");
        if (map.Environment.Rain > 1 || map.Environment.Snow > 1 || map.Environment.Fog > 1)
            issues.Add("CSM-META", AuditSeverity.Warning,
                $"Clima fuera de 0/1: lluvia={map.Environment.Rain} nieve={map.Environment.Snow} niebla={map.Environment.Fog}.");
        if (!string.IsNullOrWhiteSpace(info.Restriction) &&
            !byte.TryParse(info.Restriction, out _) &&
            !info.Restriction.Equals("NEWBIE", StringComparison.OrdinalIgnoreCase))
            issues.Add("CSM-META", AuditSeverity.Warning, $"Restriccion no numerica: '{info.Restriction}'.");
        if (!string.IsNullOrWhiteSpace(info.Exit))
        {
            var fields = info.Exit.Split('-');
            if (fields.Length != 3 ||
                !short.TryParse(fields[0], out _) || !short.TryParse(fields[1], out _) || !short.TryParse(fields[2], out _))
                issues.Add("CSM-META", AuditSeverity.Warning, $"Salida legacy mal formada: '{info.Exit}'.");
        }
        var minLevel = info.Level & 0xFF;
        var maxLevel = (info.Level >> 8) & 0xFF;
        if (maxLevel > 0 && minLevel > maxLevel)
            issues.Add("CSM-META", AuditSeverity.Warning, $"Nivel minimo {minLevel} mayor que maximo {maxLevel}.");
        else if (maxLevel == 0 && minLevel > 0)
            issues.Add("CSM-META", AuditSeverity.Info, $"Nivel minimo {minLevel} sin maximo explicito (0).");
    }

    private static void CheckContentBounds(WorldSector map, RawAudit? raw, Issues issues)
    {
        if (raw is not { SizeValid: true }) return;
        var minX = 101; var maxX = 0; var minY = 101; var maxY = 0;
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
        {
            var block = map.Blocks[x, y];
            if (!HasContent(block)) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (maxX == 0) return;
        if (minX < raw.Size.XMin || maxX > raw.Size.XMax || minY < raw.Size.YMin || maxY > raw.Size.YMax)
            issues.Add("CSM-BOUNDS", AuditSeverity.Warning,
                $"Contenido en {minX}..{maxX},{minY}..{maxY} fuera de CsmSectorSize " +
                $"{raw.Size.XMin}..{raw.Size.XMax},{raw.Size.YMin}..{raw.Size.YMax}.");
    }

    private void CheckReferences(WorldSector map, Issues issues)
    {
        if (_catalog is null) return;
        var graphics = new HashSet<int>();
        var objects = new HashSet<int>();
        var npcs = new HashSet<int>();
        var triggers = new HashSet<int>();
        var particles = new HashSet<int>();
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
        {
            var block = map.Blocks[x, y];
            for (var layer = 0; layer < 4; layer++)
                if (block.Graphics[layer].GraphicIndex > 0) graphics.Add(block.Graphics[layer].GraphicIndex);
            if (block.Object.Index > 0) objects.Add(block.Object.Index);
            if (block.NpcIndex > 0) npcs.Add(block.NpcIndex);
            if (block.Trigger != 0) triggers.Add(block.Trigger);
            if (block.ParticleIndex != 0) particles.Add(block.ParticleIndex);
        }

        var missingGraphics = new List<int>();
        var missingFiles = new List<int>();
        foreach (var index in graphics)
        {
            if (index <= 0 || index >= _catalog.Graphics.Count) { missingGraphics.Add(index); continue; }
            var data = _catalog.Graphics[index];
            if (data.FileNumber <= 0 && data.Frames.Length == 0) { missingGraphics.Add(index); continue; }
            if (data.FileNumber > 0 && _resolver is not null)
            {
                try { _resolver.Resolve(data); }
                catch { missingFiles.Add(data.FileNumber); }
            }
        }
        AddAggregate(issues, "CSM-GRH", AuditSeverity.Warning, "GRH fuera de indice", missingGraphics);
        AddAggregate(issues, "CSM-GRH-FILE", AuditSeverity.Warning, "PNG de GRH faltante", missingFiles);

        var missingObjects = objects.Where(index => index >= _catalog.Objects.Count).ToList();
        AddAggregate(issues, "CSM-OBJECT", AuditSeverity.Warning, "Objeto fuera de obj.dat", missingObjects);

        var missingNpcs = npcs.Where(index => index >= _catalog.Npcs.Count).ToList();
        AddAggregate(issues, "CSM-NPC", AuditSeverity.Warning, "NPC fuera de NPCs.dat", missingNpcs);
        var missingBodies = new List<int>();
        var missingHeads = new List<int>();
        foreach (var index in npcs.Where(index => index > 0 && index < _catalog.Npcs.Count))
        {
            var npc = _catalog.Npcs[index];
            if (npc.Body >= _catalog.Bodies.Length) missingBodies.Add(npc.Body);
            if (npc.Head >= _catalog.Heads.Length) missingHeads.Add(npc.Head);
        }
        AddAggregate(issues, "CSM-NPC-BODY", AuditSeverity.Warning, "Body de NPC sin indice", missingBodies);
        AddAggregate(issues, "CSM-NPC-HEAD", AuditSeverity.Warning, "Head de NPC sin indice", missingHeads);

        if (_triggerIndexes is not null)
        {
            var missingTriggers = triggers.Where(trigger => !_triggerIndexes.Contains(trigger)).ToList();
            AddAggregate(issues, "CSM-TRIGGER", AuditSeverity.Info,
                "Trigger sin nombre en triggers.dat", missingTriggers);
        }

        var missingParticles = particles.Where(index => index < 1 || index > _catalog.Particles.Count).ToList();
        AddAggregate(issues, "CSM-PARTICLE", AuditSeverity.Warning, "Particula fuera de particles.ind", missingParticles);
    }

    private static void CheckExits(WorldSector map, IReadOnlyCollection<short>? availableMaps, Issues issues)
    {
        var available = availableMaps is null ? null : availableMaps as IReadOnlySet<short> ?? availableMaps.ToHashSet();
        var missingDestinations = new HashSet<short>();
        var specialDestinations = new HashSet<short>();
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
        {
            var exit = map.Blocks[x, y].TileExit;
            if (exit.SectorId == 0)
            {
                if (exit.X != 0 || exit.Y != 0)
                    issues.Add("CSM-EXIT", AuditSeverity.Warning, "TileExit sin mapa pero con coordenadas.", x, y);
                continue;
            }
            if (exit.X is < 1 or > WorldSector.Width || exit.Y is < 1 or > WorldSector.Height)
                issues.Add("CSM-EXIT", AuditSeverity.Warning,
                    $"TileExit a mapa {exit.SectorId} con destino fuera de 1..100: {exit.X},{exit.Y}.", x, y);
            if (exit.SectorId < 0)
                specialDestinations.Add(exit.SectorId);
            else if (available is not null && !available.Contains(exit.SectorId))
                missingDestinations.Add(exit.SectorId);
        }
        AddAggregate(issues, "CSM-EXIT-SPECIAL", AuditSeverity.Info,
            "TileExit a mapa especial negativo", specialDestinations);
        AddAggregate(issues, "CSM-EXIT-DEST", AuditSeverity.Warning,
            "TileExit a mapa sin archivo en la carpeta", missingDestinations);
    }

    private void CheckRoundTrip(WorldSector map, Issues issues)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ao-audit-{Guid.NewGuid():N}.csm");
        try
        {
            _maps.Save(map, path);
            var reloaded = _maps.Load(path);
            var mismatch = FirstRoundTripMismatch(map, reloaded);
            if (mismatch is not null)
                issues.Add("CSM-ROUNDTRIP", AuditSeverity.Error, mismatch);
        }
        catch (Exception error)
        {
            issues.Add("CSM-ROUNDTRIP", AuditSeverity.Error, $"Fallo el round-trip: {error.Message}");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static string? FirstRoundTripMismatch(WorldSector expected, WorldSector actual)
    {
        if (expected.CsmSectorSize != actual.CsmSectorSize)
            return $"CsmSectorSize {expected.CsmSectorSize} != {actual.CsmSectorSize}.";
        if (expected.Info.Name != actual.Info.Name) return "Name distinto.";
        if (expected.Info.Backup != actual.Info.Backup) return "Backup distinto.";
        if (expected.Info.Restriction != actual.Info.Restriction) return "Restriction distinta.";
        if (expected.Info.MusicNumberHigh != actual.Info.MusicNumberHigh) return "MusicNumberHigh distinto.";
        if (expected.Info.MusicNumberLow != actual.Info.MusicNumberLow) return "MusicNumberLow distinto.";
        if (expected.Info.Safety != actual.Info.Safety) return "Safety distinto.";
        if (expected.Info.Zone != actual.Info.Zone) return "Zone distinta.";
        if (expected.Info.Terrain != actual.Info.Terrain) return "Terrain distinto.";
        if (expected.Info.Ambient != actual.Info.Ambient) return "Ambient distinto.";
        if (expected.Environment.BaseLight != actual.Environment.BaseLight) return "BaseLight distinto.";
        if (expected.Info.LetterGraphic != actual.Info.LetterGraphic) return "LetterGraphic distinto.";
        if (expected.Info.Level != actual.Info.Level) return "Level distinto.";
        if (expected.Info.Extra2 != actual.Info.Extra2) return "Extra2 distinto.";
        if (expected.Info.Exit != actual.Info.Exit) return "Exit distinto.";
        if (expected.Environment.Rain != actual.Environment.Rain) return "Rain distinto.";
        if (expected.Environment.Snow != actual.Environment.Snow) return "Snow distinto.";
        if (expected.Environment.Fog != actual.Environment.Fog) return "Fog distinto.";
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
        {
            var a = expected.Blocks[x, y];
            var b = actual.Blocks[x, y];
            if (a.Blocked != b.Blocked) return $"Blocked distinto en {x},{y}.";
            for (var layer = 0; layer < 4; layer++)
                if (a.Graphics[layer].GraphicIndex != b.Graphics[layer].GraphicIndex)
                    return $"Layer {layer + 1} distinta en {x},{y}.";
            if (a.Trigger != b.Trigger) return $"Trigger distinto en {x},{y}.";
            if (a.ParticleIndex != b.ParticleIndex) return $"Particula distinta en {x},{y}.";
            if (a.Light.Range != b.Light.Range || a.Light.Color != b.Light.Color) return $"Luz distinta en {x},{y}.";
            if (a.Object.Index != b.Object.Index || a.Object.Amount != b.Object.Amount) return $"Objeto distinto en {x},{y}.";
            if (a.NpcIndex != b.NpcIndex) return $"NPC distinto en {x},{y}.";
            if (a.TileExit.SectorId != b.TileExit.SectorId || a.TileExit.X != b.TileExit.X || a.TileExit.Y != b.TileExit.Y)
                return $"TileExit distinto en {x},{y}.";
        }
        return null;
    }

    private static IReadOnlyList<MapBorderExit> CollectBorderExits(WorldSector map)
    {
        var exits = new List<MapBorderExit>();
        for (var index = 1; index <= WorldSector.Width; index++)
        {
            var west = map.Blocks[13, index].TileExit;
            if (west.SectorId != 0 && west.X == 87)
                exits.Add(new MapBorderExit(GlobalWorldSide.West, west.SectorId, 13, index, west.X, west.Y));
            var east = map.Blocks[88, index].TileExit;
            if (east.SectorId != 0 && east.X == 14)
                exits.Add(new MapBorderExit(GlobalWorldSide.East, east.SectorId, 88, index, east.X, east.Y));
            var north = map.Blocks[index, 10].TileExit;
            if (north.SectorId != 0 && north.Y == 90)
                exits.Add(new MapBorderExit(GlobalWorldSide.North, north.SectorId, index, 10, north.X, north.Y));
            var south = map.Blocks[index, 91].TileExit;
            if (south.SectorId != 0 && south.Y == 11)
                exits.Add(new MapBorderExit(GlobalWorldSide.South, south.SectorId, index, 91, south.X, south.Y));
        }
        return exits;
    }

    private static bool HasContent(SectorTile block)
    {
        if (block.Blocked != 0 || block.Trigger != 0 || block.Light.Range != 0 ||
            block.ParticleIndex != 0 || block.NpcIndex != 0 || block.Object.Index != 0 ||
            block.TileExit.SectorId != 0) return true;
        for (var layer = 0; layer < 4; layer++)
            if (block.Graphics[layer].GraphicIndex != 0) return true;
        return false;
    }

    private static (int Blocked, int Triggers, int Lights, int Particles, int Npcs, int Objects, int Exits) CountPersisted(WorldSector map)
    {
        var blocked = 0; var triggers = 0; var lights = 0; var particles = 0;
        var npcs = 0; var objects = 0; var exits = 0;
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
        {
            var block = map.Blocks[x, y];
            if (block.Blocked != 0) blocked++;
            if (block.Trigger != 0) triggers++;
            if (block.Light.Range != 0) lights++;
            if (block.ParticleIndex != 0) particles++;
            if (block.NpcIndex != 0) npcs++;
            if (block.Object.Index != 0) objects++;
            if (block.TileExit.SectorId != 0) exits++;
        }
        return (blocked, triggers, lights, particles, npcs, objects, exits);
    }

    private static void AddAggregate(Issues issues, string code, AuditSeverity severity, string label,
        IEnumerable<int> values)
    {
        var list = values.Distinct().OrderBy(value => value).ToArray();
        if (list.Length == 0) return;
        issues.Add(code, severity, $"{label}: {list.Length} distinto(s) [{string.Join(",", list.Take(12))}]" +
            (list.Length > 12 ? ",..." : string.Empty));
    }

    private static void AddAggregate(Issues issues, string code, AuditSeverity severity, string label,
        IEnumerable<short> values)
    {
        AddAggregate(issues, code, severity, label, values.Select(value => (int)value));
    }

    private static RawAudit ReadRaw(string path, Issues issues)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.Default, leaveOpen: false);
        var counts = new int[11];
        for (var index = 0; index < counts.Length; index++)
        {
            counts[index] = reader.ReadInt32();
            if (counts[index] is < 0 or > MaxRecords)
                throw new InvalidDataException($"Count {index} invalido: {counts[index]}.");
        }
        var size = new CsmSectorSize(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
        // The VB6 CSM writer puts MapSize 0,0,0,0 for a full 100x100 map; the
        // loader normalizes it to the default. Treat it as valid.
        var zeroSize = size.XMin == 0 && size.XMax == 0 && size.YMin == 0 && size.YMax == 0;
        var effectiveSize = size.IsValid ? size : zeroSize ? CsmSectorSize.Default : size;
        var sizeValid = size.IsValid || zeroSize;
        if (!sizeValid)
            issues.Add("CSM-SIZE", AuditSeverity.Error,
                $"CsmSectorSize invalido: {size.XMin}..{size.XMax},{size.YMin}..{size.YMax}.");

        SkipString(reader); reader.ReadByte();
        SkipString(reader); reader.ReadInt32(); reader.ReadInt32(); reader.ReadByte();
        SkipString(reader); SkipString(reader); SkipString(reader);
        reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt32();
        SkipString(reader); reader.ReadByte(); reader.ReadByte(); reader.ReadByte();

        var unique = new int[11];
        var blockedSeen = new HashSet<(int, int)>();
        var triggerSeen = new HashSet<(int, int)>();
        var lightSeen = new HashSet<(int, int)>();
        var particleSeen = new HashSet<(int, int)>();
        var npcSeen = new HashSet<(int, int)>();
        var objectSeen = new HashSet<(int, int)>();
        var exitSeen = new HashSet<(int, int)>();
        var layerSeen = new[] { new HashSet<(int, int)>(), new HashSet<(int, int)>(), new HashSet<(int, int)>(), new HashSet<(int, int)>() };
        ReadRecords(counts[0], () =>
        {
            var x = reader.ReadInt16(); var y = reader.ReadInt16(); reader.ReadByte();
            CheckPosition(x, y, effectiveSize, "bloqueo", issues);
            if (blockedSeen.Add((x, y))) unique[0]++; else Duplicate("bloqueo", x, y, issues);
        });
        for (var layer = 0; layer < 4; layer++)
        {
            var current = layer;
            ReadRecords(counts[1 + current], () =>
            {
                var x = reader.ReadInt16(); var y = reader.ReadInt16(); reader.ReadInt32();
                CheckPosition(x, y, effectiveSize, $"capa {current + 1}", issues);
                if (layerSeen[current].Add((x, y))) unique[1 + current]++;
                else Duplicate($"capa {current + 1}", x, y, issues);
            });
        }
        ReadRecords(counts[5], () =>
        {
            var x = reader.ReadInt16(); var y = reader.ReadInt16(); reader.ReadInt16();
            CheckPosition(x, y, effectiveSize, "trigger", issues);
            if (triggerSeen.Add((x, y))) unique[5]++; else Duplicate("trigger", x, y, issues);
        });
        // VB6 writes particles before lights in the payload; the header counts
        // keep the opposite order (lights, particles).
        ReadRecords(counts[7], () =>
        {
            var x = reader.ReadInt16(); var y = reader.ReadInt16(); reader.ReadInt32();
            CheckPosition(x, y, effectiveSize, "particula", issues);
            if (particleSeen.Add((x, y))) unique[7]++; else Duplicate("particula", x, y, issues);
        });
        ReadRecords(counts[6], () =>
        {
            var x = reader.ReadInt16(); var y = reader.ReadInt16(); reader.ReadInt32(); reader.ReadByte();
            CheckPosition(x, y, effectiveSize, "luz", issues);
            if (lightSeen.Add((x, y))) unique[6]++; else Duplicate("luz", x, y, issues);
        });
        ReadRecords(counts[9], () =>
        {
            var x = reader.ReadInt16(); var y = reader.ReadInt16(); reader.ReadInt16(); reader.ReadInt16();
            CheckPosition(x, y, effectiveSize, "objeto", issues);
            if (objectSeen.Add((x, y))) unique[9]++; else Duplicate("objeto", x, y, issues);
        });
        ReadRecords(counts[8], () =>
        {
            var x = reader.ReadInt16(); var y = reader.ReadInt16(); reader.ReadInt16();
            CheckPosition(x, y, effectiveSize, "NPC", issues);
            if (npcSeen.Add((x, y))) unique[8]++; else Duplicate("NPC", x, y, issues);
        });
        ReadRecords(counts[10], () =>
        {
            var x = reader.ReadInt16(); var y = reader.ReadInt16();
            var map = reader.ReadInt16(); var destX = reader.ReadInt16(); var destY = reader.ReadInt16();
            CheckPosition(x, y, effectiveSize, "exit", issues);
            if (exitSeen.Add((x, y))) unique[10]++; else Duplicate("exit", x, y, issues);
            if (map == 0 && (destX != 0 || destY != 0))
                issues.Add("CSM-EXIT", AuditSeverity.Warning, "TileExit sin mapa pero con coordenadas.", x, y);
            if (map != 0 && (destX is < 1 or > WorldSector.Width || destY is < 1 or > WorldSector.Height))
                issues.Add("CSM-EXIT", AuditSeverity.Warning,
                    $"TileExit a mapa {map} con destino fuera de 1..100: {destX},{destY}.", x, y);
        });

        if (reader.BaseStream.Position != reader.BaseStream.Length)
            issues.Add("CSM-TRAILING", AuditSeverity.Info,
                $"Quedan {reader.BaseStream.Length - reader.BaseStream.Position} bytes de un guardado VB6 anterior; " +
                "el loader los ignora y el escritor C# trunca al guardar.");
        return new RawAudit(counts, effectiveSize, sizeValid, unique);
    }

    private static void ReadRecords(int count, Action read)
    {
        if (count is < 0 or > MaxRecords) throw new InvalidDataException($"Cantidad de registros invalida: {count}.");
        for (var index = 0; index < count; index++) read();
    }

    private static void CheckPosition(short x, short y, CsmSectorSize size, string kind, Issues issues)
    {
        if (x is < 1 or > WorldSector.Width || y is < 1 or > WorldSector.Height)
        {
            issues.Add("CSM-RANGE", AuditSeverity.Warning, $"{kind} fuera de 1..100 en {x},{y}.", x, y);
            return;
        }
        if (size.IsValid && (x < size.XMin || x > size.XMax || y < size.YMin || y > size.YMax))
            issues.Add("CSM-BOUNDS", AuditSeverity.Warning,
                $"{kind} en {x},{y} fuera de CsmSectorSize {size.XMin}..{size.XMax},{size.YMin}..{size.YMax}.", x, y);
    }

    private static void Duplicate(string kind, int x, int y, Issues issues) =>
        issues.Add("CSM-DUP", AuditSeverity.Warning, $"{kind} duplicado en {x},{y}.", x, y);

    private static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("String CSM incompleta.");
    }

    private sealed record RawAudit(int[] Counts, CsmSectorSize Size, bool SizeValid, int[] Unique);

    private sealed class Issues
    {
        private readonly short _mapId;
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _suppressed = new(StringComparer.Ordinal);
        public List<SectorAuditIssue> List { get; } = [];

        public Issues(short mapId) => _mapId = mapId;

        public void Add(string code, AuditSeverity severity, string message, int x = 0, int y = 0, int layer = -1)
        {
            _counts[code] = _counts.GetValueOrDefault(code) + 1;
            if (_counts[code] > MaxIssuesPerCode)
            {
                _suppressed[code] = _suppressed.GetValueOrDefault(code) + 1;
                return;
            }
            List.Add(new SectorAuditIssue(_mapId, code, severity, message, x, y, layer));
        }

        public void Flush()
        {
            foreach (var (code, count) in _suppressed)
                List.Add(new SectorAuditIssue(_mapId, code, AuditSeverity.Info,
                    $"{count} caso(s) adicionales del mismo codigo omitidos."));
        }
    }
}
