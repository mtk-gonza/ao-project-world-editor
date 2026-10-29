using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using System.Drawing;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapMiniMapTests
{
    [Fact]
    public void Minimap_uses_top_layer_color_and_red_for_full_block()
    {
        var map = new WorldMap(); map.Blocks[3, 4].Graphics[0].GraphicIndex = 1; map.Blocks[3, 4].Blocked = 0xF;
        var graphics = new[] { new GraphicData(), new GraphicData { MiniMapColor = unchecked((int)0x0000FF00) } };
        using var image = new MapMiniMapService().Create(map, graphics, false);
        Assert.Equal(Color.FromArgb(255, 0, 255, 0).ToArgb(), image.GetPixel(2, 3).ToArgb());
        using var blocked = new MapMiniMapService().Create(map, graphics, true);
        Assert.Equal(Color.Red.ToArgb(), blocked.GetPixel(2, 3).ToArgb());
        Assert.Equal(100, blocked.Width); Assert.Equal(100, blocked.Height);
    }

    [Fact]
    public void Minimap_applies_vb6_overlay_priority_and_any_block_mask()
    {
        var map = new WorldMap();
        var block = map.Blocks[3, 4];
        block.Graphics[0].GraphicIndex = 1;
        block.Graphics[1].GraphicIndex = 2;
        block.NpcIndex = 7;
        block.ParticleIndex = 9;
        block.Blocked = 1;
        var graphics = new[]
        {
            new GraphicData(),
            new GraphicData { MiniMapColor = 0x000000FF },
            new GraphicData { MiniMapColor = 0x0000FF00 }
        };

        using var image = new MapMiniMapService().Create(map, graphics,
            new MapMiniMapOptions(ShowBlocked: true, ShowNpcs: true, ShowParticles: true));

        Assert.Equal(Color.White.ToArgb(), image.GetPixel(2, 3).ToArgb());
        Assert.Equal(Color.FromArgb(255, 0, 0, 255), MapMiniMapService.FromColorRef(0x000000FF));
    }

    [Fact]
    public void Minimap_leaves_tiles_outside_csm_domain_black()
    {
        var map = new WorldMap { CsmMapSize = new CsmMapSize(3, 2, 3, 2) };
        map.Blocks[1, 1].Graphics[0].GraphicIndex = 1;
        map.Blocks[2, 2].Graphics[0].GraphicIndex = 1;
        var graphics = new[] { new GraphicData(), new GraphicData { MiniMapColor = 0x000000FF } };

        using var image = new MapMiniMapService().Create(map, graphics, false);

        Assert.Equal(Color.Black.ToArgb(), image.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), image.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void Minimap_does_not_erase_lower_layer_when_upper_grh_has_no_color()
    {
        var map = new WorldMap();
        map.Blocks[3, 4].Graphics[0].GraphicIndex = 1;
        map.Blocks[3, 4].Graphics[1].GraphicIndex = 2;
        var graphics = new[]
        {
            new GraphicData(),
            new GraphicData { MiniMapColor = 0x000000FF },
            new GraphicData { MiniMapColor = 0 }
        };

        using var image = new MapMiniMapService().Create(map, graphics, false);

        Assert.Equal(Color.Blue.ToArgb(), image.GetPixel(2, 3).ToArgb());
    }

    [Fact]
    public void Minimap_default_options_match_vb6_layer1_and_layer2_only()
    {
        var map = new WorldMap();
        map.Blocks[3, 4].Graphics[0].GraphicIndex = 1;
        map.Blocks[3, 4].Graphics[2].GraphicIndex = 2;
        map.Blocks[3, 4].Graphics[3].GraphicIndex = 2;
        var graphics = new[]
        {
            new GraphicData(),
            new GraphicData { MiniMapColor = 0x000000FF },
            new GraphicData { MiniMapColor = 0x00FF0000 }
        };

        using var image = new MapMiniMapService().Create(map, graphics, MapMiniMapOptions.Vb6Default);

        Assert.Equal(Color.Blue.ToArgb(), image.GetPixel(2, 3).ToArgb());
    }

}
