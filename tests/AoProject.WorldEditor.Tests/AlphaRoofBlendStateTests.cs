using Vortice.Direct3D11;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class AlphaRoofBlendStateTests
{
    [Fact]
    public void Additive_preset_is_one_plus_one_with_blending_enabled()
    {
        var description = new BlendDescription(Blend.One, Blend.One);
        var target = description.RenderTarget[0];

        Assert.True(target.BlendEnable);
        Assert.Equal(Blend.One, target.SourceBlend);
        Assert.Equal(Blend.One, target.DestinationBlend);
        Assert.Equal(BlendOperation.Add, target.BlendOperation);
    }

    [Fact]
    public void Alpha_preset_keeps_blending_enabled_for_the_regular_layers()
    {
        var target = BlendDescription.AlphaBlend.RenderTarget[0];
        Assert.True(target.BlendEnable);
    }
}
