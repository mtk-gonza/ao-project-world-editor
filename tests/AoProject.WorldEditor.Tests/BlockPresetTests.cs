using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class BlockPresetTests
{
    [Fact]
    public void Loader_reads_reference_block_database()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "argentum-online-worldeditor", "bloq_database.txt"));
        var presets = new BlockPresetLoader().Load(path);

        Assert.Equal(57, presets.Count);
        Assert.Contains(presets, preset => preset.Name == "Agua" && preset.TilesX == 4 && preset.Layer == 1);
        Assert.All(presets, preset => Assert.True(preset.TilesX > 0 && preset.TilesY > 0));
    }
}
