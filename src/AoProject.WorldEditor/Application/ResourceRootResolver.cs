namespace AoProject.WorldEditor.Application;

internal static class ResourceRootResolver
{
    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("AO_RESOURCES");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "ao-project-resources");
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
        }

        var sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ao-project-resources"));
        return Directory.Exists(sibling) ? sibling : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "ao-project-resources"));
    }
}
