using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

/// <summary>
/// Char_Render (Graficos_Renderizado.bas:1413-1483) has a different draw order
/// per heading and two pixel adjustments (body shift and dwarf weapon offset).
/// </summary>
public sealed class CharacterRenderOrderTests
{
    private const int BodyGrh = 100;
    private const int HeadGrh = 200;
    private const int HelmetGrh = 300;
    private const int WeaponGrh = 400;
    private const int ShieldGrh = 500;

    [Theory]
    [InlineData((byte)Heading.East, new[] { ShieldGrh, BodyGrh, HeadGrh, HelmetGrh, WeaponGrh })]
    [InlineData((byte)Heading.North, new[] { WeaponGrh, ShieldGrh, BodyGrh, HeadGrh, HelmetGrh })]
    [InlineData((byte)Heading.West, new[] { WeaponGrh, BodyGrh, HeadGrh, HelmetGrh, ShieldGrh })]
    [InlineData((byte)Heading.South, new[] { BodyGrh, HeadGrh, HelmetGrh, ShieldGrh, WeaponGrh })]
    public void Draws_equipment_in_the_vb6_order_for_each_heading(byte headingValue, int[] expected)
    {
        var sprites = Render((Heading)headingValue, body: 1, bodyHeadOffsetY: -27);

        Assert.Equal(expected, sprites.Select(sprite => sprite.GraphicIndex).ToArray());
    }

    [Theory]
    [InlineData(1, 1)]      // body < 488 -> one pixel right
    [InlineData(487, 1)]
    [InlineData(488, 0)]
    [InlineData(600, 0)]
    public void Shifts_the_body_one_pixel_for_low_body_ids(short body, int expectedShift)
    {
        var sprites = Render(Heading.South, body, bodyHeadOffsetY: -27);

        var bodySprite = sprites.First(sprite => sprite.GraphicIndex == BodyGrh);
        Assert.Equal((5 - 1) * 32 + expectedShift, bodySprite.PixelX);
    }

    [Fact]
    public void Raises_weapon_and_shield_by_seven_pixels_for_dwarves()
    {
        // A dwarf body has HeadOffset.y = -26 (Protocol.bas:2586).
        var south = Render(Heading.South, body: 1, bodyHeadOffsetY: -26);
        Assert.Equal((6 - 1) * 32 + 7, south.First(sprite => sprite.GraphicIndex == WeaponGrh).PixelY);
        Assert.Equal((6 - 1) * 32 + 7, south.First(sprite => sprite.GraphicIndex == ShieldGrh).PixelY);

        var east = Render(Heading.East, body: 1, bodyHeadOffsetY: -26);
        Assert.Equal((6 - 1) * 32 + 7, east.First(sprite => sprite.GraphicIndex == WeaponGrh).PixelY);
        Assert.Equal((6 - 1) * 32, east.First(sprite => sprite.GraphicIndex == ShieldGrh).PixelY);

        var west = Render(Heading.West, body: 1, bodyHeadOffsetY: -26);
        Assert.Equal((6 - 1) * 32, west.First(sprite => sprite.GraphicIndex == WeaponGrh).PixelY);
        Assert.Equal((6 - 1) * 32 + 7, west.First(sprite => sprite.GraphicIndex == ShieldGrh).PixelY);
    }

    [Fact]
    public void Keeps_weapon_and_shield_on_the_tile_for_normal_bodies()
    {
        var sprites = Render(Heading.South, body: 1, bodyHeadOffsetY: -27);

        Assert.Equal((6 - 1) * 32, sprites.First(sprite => sprite.GraphicIndex == WeaponGrh).PixelY);
        Assert.Equal((6 - 1) * 32, sprites.First(sprite => sprite.GraphicIndex == ShieldGrh).PixelY);
    }

    [Fact]
    public void Head_and_helmet_use_the_body_head_offset_and_do_not_animate()
    {
        var sprites = Render(Heading.North, body: 1, bodyHeadOffsetY: -27, headOffsetX: -3);

        var head = sprites.First(sprite => sprite.GraphicIndex == HeadGrh);
        var helmet = sprites.First(sprite => sprite.GraphicIndex == HelmetGrh);
        Assert.Equal((5 - 1) * 32 - 3, head.PixelX);
        Assert.Equal((6 - 1) * 32 - 27, head.PixelY);
        Assert.Equal(head.PixelX, helmet.PixelX);
        Assert.Equal(head.PixelY, helmet.PixelY);
        Assert.False(head.Animate);
        Assert.False(helmet.Animate);
    }

    private static IReadOnlyList<RenderSprite> Render(Heading heading, short body, short bodyHeadOffsetY,
        short headOffsetX = 0)
    {
        var list = new CharacterListService();
        list.Make(1, body, 1, (byte)heading, 5, 6, weapon: 1, shield: 1, helmet: 1);
        var bodies = Enumerable.Range(0, Math.Max(2, body + 1))
            .Select(_ => new DirectionalAnimation()).ToArray();
        bodies[body] = Animation(BodyGrh, headOffsetX, bodyHeadOffsetY);
        var heads = new[] { new HeadIndexData(), Head(HeadGrh) };
        var helmets = new[] { new DirectionalAnimation(), Animation(HelmetGrh, 0, 0) };
        var weapons = new[] { new DirectionalAnimation(), Animation(WeaponGrh, 0, 0) };
        var shields = new[] { new DirectionalAnimation(), Animation(ShieldGrh, 0, 0) };

        return new CharacterRenderService().Render(list.Characters, bodies, heads, helmets, weapons, shields);
    }

    private static DirectionalAnimation Animation(int grh, short headOffsetX, short headOffsetY)
    {
        var body = new DirectionalAnimation
        {
            HeadOffset = new Position(headOffsetX, headOffsetY)
        };
        for (var heading = 1; heading <= 4; heading++) body.Walk[heading].GraphicIndex = grh;
        return body;
    }

    private static HeadIndexData Head(int grh)
    {
        var head = new HeadIndexData();
        for (var heading = 1; heading <= 4; heading++) head.Head[heading].GraphicIndex = grh;
        return head;
    }
}
