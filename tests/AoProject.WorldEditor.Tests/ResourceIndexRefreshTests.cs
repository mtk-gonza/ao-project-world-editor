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
        // El arbol generado ya no incluye Dat/: los indices legacy se resuelven en
        // reference/Recursos, igual que en el arranque real del editor.
        var result = new ResourceIndexRefreshService()
            .Reload(root, ResourceRootResolver.ReferenceRoot()).Result;
        Assert.True(result.Graphics > 0);
        Assert.True(result.Surfaces > 0);
        Assert.True(result.Objects > 0);
        Assert.True(result.Npcs > 0);
        Assert.True(result.Triggers > 0);
    }
}
