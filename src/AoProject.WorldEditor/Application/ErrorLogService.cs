using System.Globalization;
using AoProject.WorldEditor.Infrastructure.Runtime;

namespace AoProject.WorldEditor.Application;

/// <summary>Port of Logs.RegistrarError: append-only editor error log.</summary>
internal sealed class ErrorLogService
{
    public ErrorLogService(string? directory = null)
    {
        Directory = directory ?? EditorPaths.LogsDirectory;
    }

    public string Directory { get; }
    public string FilePath => Path.Combine(Directory, "errores.log");

    public void Register(int errorNumber, string description, string procedure, long line = 0)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(procedure);
        System.IO.Directory.CreateDirectory(Directory);
        var stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        var message = $"{stamp} {procedure} ({errorNumber}) {description} [line {line}]{Environment.NewLine}";
        File.AppendAllText(FilePath, message);
    }
}
