using System.Text;
using System.Text.RegularExpressions;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed record LooseMapTeleportAuditEntry(
    short SourceSectorId,
    int SourceX,
    int SourceY,
    short TargetSectorId,
    short TargetX,
    short TargetY,
    string Classification);

internal sealed class LooseMapTeleportAuditService
{
    private static readonly Regex SectorIdPattern = new(@"(?<!\d)\d+", RegexOptions.Compiled);
    public IReadOnlyList<LooseMapTeleportAuditEntry> Audit(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var files = Directory.EnumerateFiles(directory, "*.csm", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Id: ParseSectorId(path)))
            .Where(item => item.Id > 0)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToArray();
        var available = files
            .Where(item => new FileInfo(item.Path).Length > 0)
            .Select(item => item.Id)
            .ToHashSet();
        var result = files.AsParallel()
            .WithDegreeOfParallelism(Math.Max(1, Math.Min(Environment.ProcessorCount, 8)))
            .SelectMany(file => AuditMap(file.Path, file.Id, available))
            .ToList();
        return result.OrderBy(item => item.Classification)
            .ThenBy(item => item.SourceSectorId)
            .ThenBy(item => item.SourceY)
            .ThenBy(item => item.SourceX)
            .ToArray();
    }

    private static IReadOnlyList<LooseMapTeleportAuditEntry> AuditMap(
        string path, short mapId, IReadOnlySet<short> available)
    {
        var result = new List<LooseMapTeleportAuditEntry>();
        try
        {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.Latin1);
        var counts = Enumerable.Range(0, 11).Select(_ => reader.ReadInt32()).ToArray();
        _ = reader.ReadBytes(8); // CsmSectorSize
        SkipString(reader); _ = reader.ReadByte(); SkipString(reader);
        _ = reader.ReadInt32(); _ = reader.ReadInt32(); _ = reader.ReadByte();
        SkipString(reader); SkipString(reader); SkipString(reader);
        _ = reader.ReadInt32(); _ = reader.ReadInt32(); _ = reader.ReadInt32(); _ = reader.ReadInt32();
        SkipString(reader); _ = reader.ReadByte(); _ = reader.ReadByte(); _ = reader.ReadByte();
        Skip(reader, counts[0], 5);
        for (var layer = 0; layer < 4; layer++) Skip(reader, counts[1 + layer], 8);
        Skip(reader, counts[5], 6);
        Skip(reader, counts[7], 8);
        Skip(reader, counts[6], 9);
        Skip(reader, counts[9], 8);
        Skip(reader, counts[8], 6);
        for (var i = 0; i < counts[10]; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var targetMap = reader.ReadInt16();
            var targetX = reader.ReadInt16();
            var targetY = reader.ReadInt16();
            if (targetMap == 0) continue;
            var classification = targetMap < 0
                ? "MAPA-ESPECIAL-NEGATIVO"
                : !available.Contains(targetMap)
                    ? "MAPA-AUSENTE-EN-MAPAS"
                    : targetX is < 1 or > 100 || targetY is < 1 or > 100
                        ? "DESTINO-FUERA-DE-RANGO"
                        : "NORMAL";
            if (classification != "NORMAL")
                result.Add(new LooseMapTeleportAuditEntry(mapId, x, y,
                    targetMap, targetX, targetY, classification));
        }
        return result;
        }
        catch (EndOfStreamException)
        {
            return [new LooseMapTeleportAuditEntry(mapId, 0, 0, 0, 0, 0, "CSM-INVALIDO-INCOMPLETO")];
        }
        catch (InvalidDataException)
        {
            return [new LooseMapTeleportAuditEntry(mapId, 0, 0, 0, 0, 0, "CSM-INVALIDO-FORMATO")];
        }
    }

    private static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        Skip(reader, length, 1);
    }

    private static void Skip(BinaryReader reader, int count, int bytesPerRecord)
    {
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("Cantidad CSM inválida.");
        reader.BaseStream.Seek(checked((long)count * bytesPerRecord), SeekOrigin.Current);
    }

    public void SaveTsv(string path, IReadOnlyList<LooseMapTeleportAuditEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var builder = new StringBuilder().AppendLine(
            "SourceSectorId\tSourceX\tSourceY\tTargetMapId\tTargetX\tTargetY\tClassification");
        foreach (var entry in entries)
            builder.Append(entry.SourceSectorId).Append('\t').Append(entry.SourceX).Append('\t')
                .Append(entry.SourceY).Append('\t').Append(entry.TargetSectorId).Append('\t')
                .Append(entry.TargetX).Append('\t').Append(entry.TargetY).Append('\t')
                .Append(entry.Classification).AppendLine();
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static short ParseSectorId(string path)
    {
        var match = SectorIdPattern.Match(Path.GetFileNameWithoutExtension(path));
        return match.Success && short.TryParse(match.Value, out var id) ? id : (short)0;
    }
}
