namespace AoProject.WorldEditor.Application;

internal static class ResourceRootResolver
{
    public static string GeneratedRoot()
    {
        var configured = Environment.GetEnvironmentVariable("AO_GENERATED_RESOURCES");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ao-project-resources"));
    }

    public static string ReferenceRoot()
    {
        var configured = Environment.GetEnvironmentVariable("AO_REFERENCE_RESOURCES");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "Recursos"));
        return Directory.Exists(root) ? root : Resolve();
    }

    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("AO_RESOURCES");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var candidates = new List<string>();
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                candidates.Add(Path.Combine(directory.FullName, "ao-project-resources"));
                candidates.Add(Path.Combine(directory.FullName, "reference", "Recursos"));
                directory = directory.Parent;
            }
        }

        var sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ao-project-resources"));
        candidates.Add(sibling);
        candidates.Add(Path.GetFullPath(Path.Combine(sibling, "..", "reference", "Recursos")));

        var usable = candidates
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // The working resource tree is authoritative for every index and asset
        // consumed by the editor. Mapas may intentionally be absent there because
        // source maps are opened from reference/Recursos separately.
        var local = usable.FirstOrDefault(path =>
            Path.GetFileName(path).Equals("ao-project-resources", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(Path.Combine(path, "init")) &&
            Directory.Exists(Path.Combine(path, "Graficos")));
        if (local is not null) return local;
        return usable.FirstOrDefault(path => Directory.Exists(Path.Combine(path, "init")))
            ?? Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "ao-project-resources"));
    }
}
