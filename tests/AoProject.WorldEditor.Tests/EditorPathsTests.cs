using AoProject.WorldEditor.Infrastructure.Runtime;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class EditorPathsTests
{
    [Fact]
    public void Logs_live_inside_the_world_editor_project()
    {
        Assert.Equal("ao-project-world-editor",
            Path.GetFileName(EditorPaths.ProjectRoot.TrimEnd(Path.DirectorySeparatorChar)));
        Assert.True(Directory.Exists(Path.Combine(EditorPaths.ProjectRoot, "docs")));
        Assert.True(Directory.Exists(Path.Combine(EditorPaths.ProjectRoot, "src", "AoProject.WorldEditor")));
        Assert.EndsWith("logs", EditorPaths.LogsDirectory.TrimEnd(Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(EditorPaths.LogsDirectory));
        Assert.EndsWith("autosave", EditorPaths.AutosaveDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }
}
