using System.Text;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Engine.Rendering;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>Single-file world container. Version 2 adds the map sidecars.</summary>
internal sealed class GlobalCsmArchiveService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AOGLOBAL");
    private const int Version = 2;
    private const int HeaderSize = 16;
    private const int EntrySize = 34; // id + csm offset/length + sidecar offset/length

    public string Save(GlobalWorldDocument world, string destination, string? datDestination = null,
        string? infDestination = null, string? mapDestination = null,
        WorldEditorResourceCatalog? catalog = null, string? referenceRoot = null,
        IReadOnlyList<GlobalResolvedWorldSnapshot.EmbeddedImage>? embeddedImages = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var maps = new List<MapEntry>();
        var csm = new CsmMapFileService();
        foreach (var mapId in world.Paths.Keys.OrderBy(id => id))
        {
            if (!world.Paths.TryGetValue(mapId, out var source)) continue;
            var payload = ReadMapPayload(world, mapId, source, csm);
            maps.Add(new MapEntry(mapId, payload, ReadSidecars(source, mapId)));
        }
        if (maps.Count == 0) throw new InvalidDataException("El WORLD no contiene mapas para guardar.");

        var fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var indexEnd = HeaderSize + EntrySize * maps.Count;
        var offset = indexEnd;
        using var stream = File.Create(fullPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic); writer.Write(Version); writer.Write(maps.Count);
        var locations = new List<(long CsmOffset, long CsmLength, long SideOffset, long SideLength)>();
        foreach (var map in maps)
        {
            var sidecar = SerializeFiles(map.Sidecars);
            locations.Add(((long)offset, map.Payload.Length, offset + map.Payload.Length, sidecar.Length));
            offset += map.Payload.Length + sidecar.Length;
        }
        for (var i = 0; i < maps.Count; i++)
        {
            writer.Write(maps[i].MapId);
            writer.Write(locations[i].CsmOffset); writer.Write(locations[i].CsmLength);
            writer.Write(locations[i].SideOffset); writer.Write(locations[i].SideLength);
        }
        foreach (var map in maps)
        {
            writer.Write(map.Payload);
            writer.Write(SerializeFiles(map.Sidecars));
        }
        var images = embeddedImages ?? BuildWorldImages(world, catalog, referenceRoot);
        WriteGlobalMetadata(fullPath, maps, world, datDestination, infDestination, mapDestination, images);
        return fullPath;
    }

    private static void WriteGlobalMetadata(string csmPath, IReadOnlyList<MapEntry> maps, GlobalWorldDocument world,
        string? datDestination = null, string? infDestination = null, string? mapDestination = null,
        IReadOnlyList<GlobalResolvedWorldSnapshot.EmbeddedImage>? images = null)
    {
        var datPath = datDestination ?? Path.ChangeExtension(csmPath, ".dat");
        var infPath = infDestination ?? Path.ChangeExtension(csmPath, ".inf");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(datPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(infPath))!);
        using (var dat = new StreamWriter(datPath, false, Encoding.Default))
        {
            dat.WriteLine("; AO World global metadata");
            dat.WriteLine("[GlobalWorld]");
            dat.WriteLine($"Maps={maps.Count}");
            dat.WriteLine($"AssemblyWidth={world.Assembly.Width}");
            dat.WriteLine($"AssemblyHeight={world.Assembly.Height}");
            dat.WriteLine($"WrapX={(world.Assembly.WrapX ? 1 : 0)}");
            dat.WriteLine();
            dat.WriteLine("[AssemblyPlacements]");
            foreach (var placement in world.Assembly.Placements.OrderBy(item => item.MapId))
                dat.WriteLine($"Mapa{placement.MapId}={placement.Chunk.X},{placement.Chunk.Y},{placement.LevelId}");
            dat.WriteLine();
            dat.WriteLine("[AssemblyLinks]");
            foreach (var link in world.Assembly.Links)
                dat.WriteLine($"{link.FromMap},{link.ToMap},{link.Side},{link.Evidence},{link.SourceX},{link.SourceY},{link.TargetX},{link.TargetY}");
            foreach (var map in maps)
            {
                if (!world.Maps.TryGetValue(map.MapId, out var resolvedMap)) continue;
                dat.WriteLine();
                dat.WriteLine($"[Mapa{map.MapId}]");
                WriteMapInfo(dat, resolvedMap.Info);
                foreach (var file in map.Sidecars.Where(file => file.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)))
                {
                    dat.WriteLine($"EmbeddedFile={file.Name}");
                    using var text = new StringReader(Encoding.Default.GetString(file.Data));
                    string? line;
                    while ((line = text.ReadLine()) is not null) dat.WriteLine(line);
                }
            }
        }

        var files = maps.SelectMany(map => map.Sidecars.Select(file =>
            new ArchiveFile($"Mapa{map.MapId}/{file.Name.Replace('\\', '/')}", file.Data))).ToArray();
        var resolved = GlobalResolvedWorldSnapshot.Serialize(world, images);
        File.WriteAllBytes(infPath, SerializeFiles(files.Append(new ArchiveFile("__global_resolved_world.bin", resolved)).ToArray()));
        if (!string.IsNullOrWhiteSpace(mapDestination))
        {
            var fullMapPath = Path.GetFullPath(mapDestination);
            Directory.CreateDirectory(Path.GetDirectoryName(fullMapPath)!);
            File.WriteAllBytes(fullMapPath, resolved);
        }
    }

    private static IReadOnlyList<GlobalResolvedWorldSnapshot.EmbeddedImage> BuildWorldImages(
        GlobalWorldDocument world, WorldEditorResourceCatalog? catalog, string? referenceRoot)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(referenceRoot)) return [];
        var layoutsPath = Path.Combine(referenceRoot, "init", "mapsworlddata.dat");
        if (!File.Exists(layoutsPath)) return [];
        var layouts = new MapsWorldDataService().Load(layoutsPath);
        if (layouts.Count == 0) return [];
        var shifts = BuildWorldImageShifts(world);
        using var composer = new WorldMiniMapComposer(layouts, MapMiniMapOptions.Vb6Default,
            catalog.Root, shifts);
        var csm = new CsmMapFileService();
        foreach (var pair in world.Paths.OrderBy(pair => pair.Key))
        {
            try
            {
                var map = world.Maps.TryGetValue(pair.Key, out var loaded)
                    ? loaded
                    : csm.Load(pair.Value);
                composer.Add(pair.Key, map, catalog.Graphics, catalog.Objects);
            }
            catch
            {
                // The archive still remains valid when one optional preview cannot be built.
            }
        }
        return composer.EncodeWorldImages()
            .Select(image => new GlobalResolvedWorldSnapshot.EmbeddedImage(image.Name, image.Data))
            .ToArray();
    }

    private static IReadOnlyDictionary<short, (int X, int Y)> BuildWorldImageShifts(GlobalWorldDocument world)
    {
        var stitcher = new WorldBorderStitcher(world.Assembly.Placements, mapId =>
        {
            if (!world.Paths.TryGetValue(mapId, out var path)) return null;
            try { return WorldBorderStitcher.Sample(new CsmMapFileService().Load(path)); }
            catch { return null; }
        });
        var shifts = stitcher.ForAll(world.Assembly.Placements.Select(item => item.Chunk));
        return world.Assembly.Placements
            .GroupBy(item => item.MapId)
            .ToDictionary(group => group.Key, group =>
            {
                var chunk = group.First().Chunk;
                var x = shifts.Columns.TryGetValue(chunk, out var column) ? column : 0;
                var y = shifts.Rows.TryGetValue(chunk, out var row) ? row : 0;
                return (x, y);
            });
    }

    private static void WriteMapInfo(StreamWriter writer, MapInfo info)
    {
        writer.WriteLine($"Name={info.Name}");
        writer.WriteLine($"Music={info.Music}");
        writer.WriteLine($"MusicNumberHigh={info.MusicNumberHigh}");
        writer.WriteLine($"MusicNumberLow={info.MusicNumberLow}");
        writer.WriteLine($"MapVersion={info.MapVersion}");
        writer.WriteLine($"PlayerKilling={(info.PlayerKilling ? 1 : 0)}");
        writer.WriteLine($"MagicDisabled={info.MagicDisabled}");
        writer.WriteLine($"InvisibilityDisabled={info.InvisibilityDisabled}");
        writer.WriteLine($"ResurrectionDisabled={info.ResurrectionDisabled}");
        writer.WriteLine($"NoEncryptMap={info.NoEncryptMap}");
        writer.WriteLine($"Terrain={info.Terrain}");
        writer.WriteLine($"Zone={info.Zone}");
        writer.WriteLine($"Restriction={info.Restriction}");
        writer.WriteLine($"Backup={info.Backup}");
        writer.WriteLine($"Safety={info.Safety}");
        writer.WriteLine($"Ambient={info.Ambient}");
        writer.WriteLine($"LetterGraphic={info.LetterGraphic}");
        writer.WriteLine($"Level={info.Level}");
        writer.WriteLine($"Extra2={info.Extra2}");
        writer.WriteLine($"Exit={info.Exit}");
        writer.WriteLine($"Light={info.Light}");
    }

    private static byte[] ReadMapPayload(GlobalWorldDocument world, short mapId, string source, CsmMapFileService csm)
    {
        if (!world.Maps.TryGetValue(mapId, out var map) || !map.Info.Changed)
            return File.ReadAllBytes(source);
        var temporary = Path.Combine(Path.GetTempPath(), $"ao-global-{Guid.NewGuid():N}.csm");
        try { csm.Save(map, temporary); return File.ReadAllBytes(temporary); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static IReadOnlyList<ArchiveFile> ReadSidecars(string csmPath, short mapId)
    {
        var directory = Path.GetDirectoryName(csmPath) ?? ".";
        if (IsGlobalWorldFileName(Path.GetFileName(csmPath)))
        {
            var embedded = new List<ArchiveFile>();
            foreach (var name in new[] { $"Mapa{mapId}.dat", $"Mapa{mapId}.inf", $"mapa{mapId}.bmp", "NameMapa.dat", "namemapa.dat", "translados.ini" })
                if (TryReadSidecar(csmPath, mapId, name, out var data)) embedded.Add(new ArchiveFile(name, data));
            return embedded;
        }
        var candidates = new[] { $"Mapa{mapId}.dat", $"Mapa{mapId}.inf", $"mapa{mapId}.dat", $"mapa{mapId}.inf" };
        var files = candidates.Select(name => Directory.EnumerateFiles(directory, name, SearchOption.TopDirectoryOnly).FirstOrDefault())
            .Where(path => path is not null).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new ArchiveFile(Path.GetFileName(path!), File.ReadAllBytes(path!))).ToList();
        var minimap = Path.Combine(Directory.GetParent(directory)?.FullName ?? directory, "Minimapas", $"mapa{mapId}.bmp");
        if (File.Exists(minimap)) files.Add(new ArchiveFile($"Minimapas/mapa{mapId}.bmp", File.ReadAllBytes(minimap)));
        var root = Directory.GetParent(directory)?.FullName ?? directory;
        foreach (var candidate in new[]
        {
            Path.Combine(root, "init", "NameMapa.dat"), Path.Combine(root, "init", "namemapa.dat"),
            Path.Combine(root, "MiniMap", "translados.ini"), Path.Combine(root, "Minimap", "translados.ini")
        })
        {
            if (File.Exists(candidate) && files.All(file => !file.Name.Equals(Path.GetFileName(candidate), StringComparison.OrdinalIgnoreCase)))
                files.Add(new ArchiveFile(Path.GetFileName(candidate), File.ReadAllBytes(candidate)));
        }
        return files;
    }

    private static bool IsGlobalWorldFileName(string fileName) =>
        fileName.Equals("world.csm", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("mapaGlobal.csm", StringComparison.OrdinalIgnoreCase);

    private static byte[] SerializeFiles(IReadOnlyList<ArchiveFile> files)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(files.Count);
        foreach (var file in files) { writer.Write(file.Name); writer.Write(file.Data.Length); writer.Write(file.Data); }
        return stream.ToArray();
    }

    public static bool IsArchive(string path)
    {
        if (!TryReadIndex(path, out _, out var stream)) return false;
        stream.Dispose();
        return true;
    }

    public static IReadOnlyList<short> ReadMapIds(string path)
    {
        if (!TryReadIndex(path, out var entries, out var stream)) return [];
        stream.Dispose();
        return entries.Select(entry => entry.MapId).ToArray();
    }

    public static bool TryRead(string path, short mapId, out byte[] payload) =>
        TryReadRange(path, mapId, out payload, sidecars: false);

    public static bool TryReadSidecar(string path, short mapId, string name, out byte[] payload)
    {
        payload = [];
        if (!TryReadRange(path, mapId, out var bundle, sidecars: true)) return false;
        using var reader = new BinaryReader(new MemoryStream(bundle), Encoding.UTF8);
        try
        {
            var fileCount = reader.ReadInt32();
            for (var i = 0; i < fileCount; i++)
            {
                var fileName = reader.ReadString(); var length = reader.ReadInt32(); var data = reader.ReadBytes(length);
                if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(fileName), Path.GetFileName(name), StringComparison.OrdinalIgnoreCase)) { payload = data; return true; }
            }
        }
        catch (EndOfStreamException) { return false; }
        return false;
    }

    public static bool TryReadInfEntry(string path, string name, out byte[] payload, string? infPath = null)
    {
        payload = [];
        infPath ??= Path.ChangeExtension(path, ".inf");
        if (!File.Exists(infPath)) return false;
        using var reader = new BinaryReader(File.OpenRead(infPath), Encoding.UTF8);
        try
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > 1_000_000) return false;
            for (var i = 0; i < count; i++)
            {
                var fileName = reader.ReadString(); var length = reader.ReadInt32();
                if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position) return false;
                var data = reader.ReadBytes(length);
                if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)) { payload = data; return true; }
            }
        }
        catch (EndOfStreamException) { return false; }
        return false;
    }

    private static bool TryReadRange(string path, short mapId, out byte[] data, bool sidecars)
    {
        data = [];
        if (!TryReadIndex(path, out var entries, out var stream)) return false;
        using (stream)
        {
            var entry = entries.FirstOrDefault(item => item.MapId == mapId);
            if (entry is null) return false;
            var offset = sidecars ? entry.SideOffset : entry.CsmOffset;
            var length = sidecars ? entry.SideLength : entry.CsmLength;
            if (length <= 0 || offset < 0 || offset > stream.Length - length) return false;
            stream.Position = offset; data = new BinaryReader(stream).ReadBytes(checked((int)length));
            return data.LongLength == length;
        }
    }

    private static bool TryReadIndex(string path, out List<IndexEntry> entries, out FileStream stream)
    {
        entries = []; stream = null!;
        try
        {
            stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(8).SequenceEqual(Magic)) { stream.Dispose(); return false; }
            var version = reader.ReadInt32();
            if (version is not (1 or Version)) { stream.Dispose(); return false; }
            var count = reader.ReadInt32();
            var entrySize = version == 1 ? 18 : EntrySize;
            if (count < 0 || count > 10000 || stream.Length < HeaderSize + (long)count * entrySize) { stream.Dispose(); return false; }
            for (var i = 0; i < count; i++)
            {
                var id = reader.ReadInt16(); var csmOffset = reader.ReadInt64(); var csmLength = reader.ReadInt64();
                entries.Add(version == 1 ? new IndexEntry(id, csmOffset, csmLength, 0, 0) :
                    new IndexEntry(id, csmOffset, csmLength, reader.ReadInt64(), reader.ReadInt64()));
            }
            return true;
        }
        catch (IOException) { stream?.Dispose(); return false; }
    }

    private sealed record MapEntry(short MapId, byte[] Payload, IReadOnlyList<ArchiveFile> Sidecars);
    private sealed record ArchiveFile(string Name, byte[] Data);
    private sealed record IndexEntry(short MapId, long CsmOffset, long CsmLength, long SideOffset, long SideLength);
}
