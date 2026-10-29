using AoProject.WorldEditor.Infrastructure;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AmbientationTests
{
    [Fact]
    public void Loader_reads_reference_ambientations()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ao-project-resources"));
        var ambientations = new AmbientationLoader().Load(root);
        Assert.NotEmpty(ambientations);
        Assert.Contains(ambientations, ambientation => ambientation.GraphicIndex > 0);
    }
}
