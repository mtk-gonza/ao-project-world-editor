namespace AoProject.WorldEditor.Infrastructure.Runtime;

/// <summary>
/// Runtime paths owned by the editor. Logs and autosave artifacts live inside the
/// project folder (mirroring the client) instead of %LOCALAPPDATA%, so a capture
/// or a crash log is easy to find next to the sources. User settings remain in
/// %LOCALAPPDATA% because they are per-user state, not build output.
/// </summary>
internal static class EditorPaths
{
    public static string ProjectRoot { get; } = ResolveProjectRoot();

    public static string LogsDirectory => EnsureDirectory(Path.Combine(ProjectRoot, "logs"));

    public static string AutosaveDirectory =>
        EnsureDirectory(Path.Combine(ProjectRoot, "artifacts", "autosave"));

    public static string BackupsDirectory =>
        EnsureDirectory(Path.Combine(ProjectRoot, "artifacts", "backups"));

    public static string WorldOutputDirectory =>
        EnsureDirectory(Path.Combine(ProjectRoot, "artifacts", "world"));

    public static string AuditDirectory =>
        EnsureDirectory(Path.Combine(ProjectRoot, "artifacts", "audit"));

    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AoProject", "WorldEditor", "WorldEditor.ini");

    public static string LogFile(string fileName) => Path.Combine(LogsDirectory, fileName);

    private static string ResolveProjectRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AoProject.WorldEditor.sln")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src", "AoProject.WorldEditor")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        // Published/deployed build without the solution: keep artifacts next to
        // the executable instead of guessing a parent directory.
        return AppContext.BaseDirectory;
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
