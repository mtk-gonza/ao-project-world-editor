using System.Text;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal sealed class MultiMapValidationService
{
    public string ValidateDirectory(string directory, IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<NpcData>? npcs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var validator = new MapValidationService();
        var output = new StringBuilder("Informe de errores de todos los mapas");
        var maps = Directory.EnumerateFiles(directory, "*.csm", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        foreach (var path in maps)
        {
            var issues = validator.Validate(new CsmMapFileService().Load(path), objects, npcs);
            output.AppendLine().AppendLine().Append(Path.GetFileName(path)).Append(" (issues: ").Append(issues.Count).Append(')');
            foreach (var issue in issues) output.Append(issue.X).Append(", ").Append(issue.Y).Append(" :::: ").AppendLine(issue.Message);
        }
        return output.ToString();
    }
}
