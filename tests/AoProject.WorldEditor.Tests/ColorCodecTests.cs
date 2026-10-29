using AoProject.WorldEditor.Application;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class ColorCodecTests
{
    [Fact]
    public void Vb_color_roundtrip_preserves_rgb_components()
    {
        var color = ColorCodec.ParseVbHex("&H0102A0");
        Assert.Equal((byte)1, color.Red); Assert.Equal((byte)2, color.Green); Assert.Equal((byte)160, color.Blue);
        Assert.Equal("&H0102A0", color.ToVbHex());
        Assert.Equal(color, ColorCodec.FromBgrLong(color.ToBgrLong()));
    }
}
