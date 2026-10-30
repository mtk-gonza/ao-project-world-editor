using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class GlobalSpriteOrderingTests
{
    [Fact]
    public void Sprite_rows_use_floor_for_negative_global_coordinates()
    {
        Assert.Equal(1, D3D11WorldRenderer.SpriteRowForPixel(0));
        Assert.Equal(0, D3D11WorldRenderer.SpriteRowForPixel(-1));
        Assert.Equal(0, D3D11WorldRenderer.SpriteRowForPixel(-32));
        Assert.Equal(-1, D3D11WorldRenderer.SpriteRowForPixel(-33));
    }
}
