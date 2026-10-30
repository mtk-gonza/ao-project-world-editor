using System.Text;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed record GlobalArchiveAuditIssue(string Code, string Message, bool Error = true);

internal sealed record GlobalArchiveAuditResult(
    string Archive,
    int Maps,
    IReadOnlyList<GlobalArchiveAuditIssue> Issues)
{
    public int Errors => Issues.Count(issue => issue.Error);
}

/// <summary>Checks the three-file global output without modifying it.</summary>
internal sealed class GlobalArchiveAuditService
{
    public GlobalArchiveAuditResult Audit(string csmPath, GlobalWorldDocument? source = null,
        string? datDestination = null, string? infDestination = null, string? mapDestination = null)
    {
        var issues = new List<GlobalArchiveAuditIssue>();
        var fullPath = Path.GetFullPath(csmPath);
        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        var datPath = datDestination ?? Path.ChangeExtension(fullPath, ".dat");
        var infPath = infDestination ?? Path.ChangeExtension(fullPath, ".inf");
        if (!File.Exists(fullPath)) issues.Add(new("CSM-MISSING", $"Falta {baseName}.csm."));
        if (!File.Exists(datPath)) issues.Add(new("DAT-MISSING", $"Falta {baseName}.dat."));
        if (!File.Exists(infPath)) issues.Add(new("INF-MISSING", $"Falta {baseName}.inf."));
        if (mapDestination is not null && !File.Exists(mapDestination))
            issues.Add(new("MAP-MISSING", $"Falta {Path.GetFileName(mapDestination)}."));
        if (issues.Any(issue => issue.Error)) return new(fullPath, 0, issues);

        IReadOnlyList<short> ids;
        try { ids = GlobalCsmArchiveService.ReadMapIds(fullPath); }
        catch (Exception error)
        {
            issues.Add(new("CSM-INDEX", $"Índice global inválido: {error.Message}"));
            return new(fullPath, 0, issues);
        }
        if (ids.Count == 0) issues.Add(new("CSM-EMPTY", "El archivo no contiene mapas."));
        if (ids.Distinct().Count() != ids.Count) issues.Add(new("CSM-DUPLICATE-ID", "Hay map IDs duplicados en el índice."));
        if (source is not null)
        {
            foreach (var id in source.Paths.Keys.Except(ids)) issues.Add(new("MAP-MISSING", $"Falta el mapa {id} en el archivo global."));
            foreach (var id in ids.Except(source.Paths.Keys)) issues.Add(new("MAP-UNEXPECTED", $"El archivo contiene el mapa {id}, no presente en el origen.", Error: false));
        }
        foreach (var id in ids)
        {
            if (!GlobalCsmArchiveService.TryRead(fullPath, id, out var payload) || payload.Length == 0)
                issues.Add(new("CSM-PAYLOAD", $"El payload del mapa {id} no se puede leer."));
            if (!GlobalCsmArchiveService.TryReadSidecar(fullPath, id, $"Mapa{id}.dat", out _) &&
                !GlobalCsmArchiveService.TryReadSidecar(fullPath, id, $"mapa{id}.dat", out _))
                issues.Add(new("DAT-MAP-MISSING", $"Falta el metadata Mapa{id}.dat embebido.", Error: false));
        }

        var dat = File.ReadAllText(datPath, Encoding.Default);
        if (!dat.Contains("[AssemblyPlacements]", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("ASSEMBLY-MISSING", $"{baseName}.dat no contiene el ensamblado visual."));
        if (!dat.Contains("[GlobalWorld]", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("GLOBAL-METADATA", $"{baseName}.dat no contiene metadata global."));

        try
        {
            var files = ReadBundle(infPath);
            if (!files.ContainsKey("__global_resolved_world.bin"))
                issues.Add(new("SNAPSHOT-MISSING", $"{baseName}.inf no contiene el snapshot resuelto."));
            else if (!files["__global_resolved_world.bin"].AsSpan(0, Math.Min(7, files["__global_resolved_world.bin"].Length)).SequenceEqual(Encoding.ASCII.GetBytes("AOWORLD")))
                issues.Add(new("SNAPSHOT-FORMAT", "El snapshot global tiene una firma inválida."));
        }
        catch (Exception error) { issues.Add(new("INF-FORMAT", $"{baseName}.inf inválido: {error.Message}")); }
        if (mapDestination is not null && File.Exists(mapDestination))
        {
            try
            {
                using var map = File.OpenRead(mapDestination);
                var magic = new byte[7];
                if (map.Read(magic, 0, magic.Length) != magic.Length ||
                    !magic.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes("AOWORLD")))
                    issues.Add(new("MAP-FORMAT", $"{Path.GetFileName(mapDestination)} no contiene un snapshot AOWORLD válido."));
            }
            catch (Exception error) { issues.Add(new("MAP-READ", $"No se pudo leer {Path.GetFileName(mapDestination)}: {error.Message}")); }
        }
        return new(fullPath, ids.Count, issues);
    }

    private static Dictionary<string, byte[]> ReadBundle(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var count = reader.ReadInt32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("Cantidad de archivos inválida.");
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString(); var length = reader.ReadInt32();
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Longitud inválida.");
            result[name] = reader.ReadBytes(length);
        }
        return result;
    }
}
