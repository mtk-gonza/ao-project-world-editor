using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class ResourceIndexRefreshTests
{
    [Fact]
    public void Refresh_reloads_all_reference_catalog_indices_without_writing_resources()
    {
        var root = Environment.GetEnvironmentVariable("AO_RESOURCES") ??
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ao-project-resources"));
        var result = new ResourceIndexRefreshService().Reload(root).Result;
        Assert.True(result.Graphics > 0);
        Assert.True(result.Surfaces > 0);
        Assert.True(result.Objects > 0);
        Assert.True(result.Npcs > 0);
        Assert.True(result.Triggers > 0);
    }
}
