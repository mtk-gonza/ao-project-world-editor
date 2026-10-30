using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Domain.Rendering;
using AoProject.WorldEditor.Engine.Rendering;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class CharacterParticleRenderTests
{
    private static DirectionalAnimation Body(int grh, int headOffsetX, int headOffsetY)
    {
        var body = new DirectionalAnimation { HeadOffset = new Position((short)headOffsetX, (short)headOffsetY) };
        for (var heading = 1; heading <= 4; heading++) body.Walk[heading].GraphicIndex = grh;
        return body;
    }

    private static HeadIndexData Head(int grh)
    {
        var head = new HeadIndexData();
        for (var heading = 1; heading <= 4; heading++) head.Head[heading].GraphicIndex = grh;
        return head;
    }

    [Fact]
    public void Character_render_emits_body_and_head_with_offset()
    {
        var list = new CharacterListService();
        list.Make(1, 1, 1, (byte)Heading.South, 5, 6);
        var sprites = new CharacterRenderService().Render(list.Characters,
            new[] { new DirectionalAnimation(), Body(100, -3, -27) },
            new[] { new HeadIndexData(), Head(200) });

        Assert.Equal(2, sprites.Count);
        Assert.Equal(100, sprites[0].GraphicIndex);
        // Body id < 488 draws one pixel to the right (Char_Render).
        Assert.Equal((5 - 1) * 32 + 1, sprites[0].PixelX);
        Assert.Equal((6 - 1) * 32, sprites[0].PixelY);
        Assert.Equal(200, sprites[1].GraphicIndex);
        Assert.Equal((5 - 1) * 32 - 3, sprites[1].PixelX);
        Assert.Equal((6 - 1) * 32 - 27, sprites[1].PixelY);
    }

    [Fact]
    public void Character_fx_adds_a_translucent_sprite()
    {
        var list = new CharacterListService();
        var entry = list.Make(1, 1, 0, (byte)Heading.North, 2, 2);
        new CharacterFxService().SetFx(entry, 555);
        var sprites = new CharacterRenderService().Render(list.Characters,
            new[] { new DirectionalAnimation(), Body(100, 0, 0) }, new[] { new HeadIndexData() });
        Assert.Contains(sprites, sprite => sprite.GraphicIndex == 555 && sprite.Alpha < 255);
    }

    [Fact]
    public void Character_with_zero_heading_is_not_rendered()
    {
        var list = new CharacterListService();
        list.Make(1, 1, 1, 0, 5, 6);

        var sprites = new CharacterRenderService().Render(list.Characters,
            new[] { new DirectionalAnimation(), Body(100, 0, 0) },
            new[] { new HeadIndexData(), Head(200) });

        Assert.Empty(sprites);
    }

    [Fact]
    public void Character_render_layers_helmet_weapon_and_shield_from_npc_indices()
    {
        var list = new CharacterListService();
        list.Make(1, 1, 1, (byte)Heading.South, 5, 6, weapon: 1, shield: 1, helmet: 1);
        var body = new[] { new DirectionalAnimation(), Body(100, -3, -27) };
        var heads = new[] { new HeadIndexData(), Head(200) };
        var helmets = new[] { new DirectionalAnimation(), Body(300, 0, 0) };
        var weapons = new[] { new DirectionalAnimation(), Body(400, 0, 0) };
        var shields = new[] { new DirectionalAnimation(), Body(500, 0, 0) };

        var sprites = new CharacterRenderService().Render(list.Characters, body, heads, helmets, weapons, shields);

        // South: body, head, helmet, shield, weapon (Char_Render).
        Assert.Equal(new[] { 100, 200, 300, 500, 400 }, sprites.Select(sprite => sprite.GraphicIndex));
        Assert.True(sprites[0].Animate);
        Assert.False(sprites[1].Animate);
        Assert.False(sprites[2].Animate);
        Assert.True(sprites[3].Animate);
        Assert.True(sprites[4].Animate);
        Assert.Equal((5 - 1) * 32 - 3, sprites[1].PixelX);
        Assert.Equal((6 - 1) * 32 - 27, sprites[1].PixelY);
    }

    [Fact]
    public void EstaPCarea_checks_map_area()
    {
        var service = new CharacterFxService();
        Assert.True(service.EstaPCarea(50, 50, 10, 90, 10, 90));
        Assert.False(service.EstaPCarea(5, 50, 10, 90, 10, 90));
        Assert.True(service.EstaPCarea(50, 50, 90, 10, 90, 10));
    }

    [Fact]
    public void Inventory_slots_follow_the_grid()
    {
        var slots = new InventoryRenderService().Slots(10, 20, 3, 2, 32);
        Assert.Equal(6, slots.Count);
        Assert.Equal((10, 20, 32, 32), slots[0]);
        Assert.Equal((10 + 2 * 34, 20 + 34, 32, 32), slots[5]);
    }

    [Fact]
    public void Particle_render_uses_catalog_frames_and_tint()
    {
        var catalog = new List<ParticleData>
        {
            new() { Name = "Fire", NumOfParticles = 2, Grhs = [10, 11], ColorTint = [0x112233, 0x445566, 0, 0], Speed = 4f }
        };
        var groups = new ParticleGroupService();
        groups.Create(10, 10, 1);
        var sprites = new ParticleRenderService().Render(groups.Groups, catalog, 0.5f);
        Assert.Equal(2, sprites.Count);
        Assert.All(sprites, sprite => Assert.Contains(sprite.GraphicIndex, new[] { 10, 11 }));
        Assert.Contains(sprites, sprite => sprite.Color == 0x332211);
    }

    [Fact]
    public void Particle_render_uses_white_when_stream_has_no_color_table()
    {
        var catalog = new List<ParticleData> { new() { NumOfParticles = 1, Grhs = [10], ColorTint = [] } };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);

        var sprite = Assert.Single(new ParticleRenderService().Render(groups.Groups, catalog, 0f));

        Assert.Equal(0xFFFFFF, sprite.Color);
    }

    [Fact]
    public void Particle_render_treats_speed_as_total_duration_in_milliseconds()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10, 11], ColorTint = [0x0000FF, 0, 0, 0], Speed = 1000f }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);

        var sprites = new ParticleRenderService().Render(groups.Groups, catalog, 0.6f);

        Assert.Single(sprites);
        Assert.Equal(11, sprites[0].GraphicIndex);
    }

    [Fact]
    public void Particles_in_one_stream_share_the_current_stream_frame()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 2, Grhs = [10, 11], ColorTint = [0x0000FF, 0, 0, 0], Speed = 1000f }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);

        var sprites = new ParticleRenderService().Render(groups.Groups, catalog, 0.6f);

        Assert.Equal(2, sprites.Count);
        Assert.All(sprites, sprite => Assert.Equal(11, sprite.GraphicIndex));
    }

    [Fact]
    public void Particle_positions_are_relative_to_the_tile_center()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10], ColorTint = [0x0000FF, 0, 0, 0] }
        };
        var groups = new ParticleGroupService();
        groups.Create(2, 3, 1);

        var sprite = Assert.Single(new ParticleRenderService().Render(groups.Groups, catalog, 0f));

        Assert.InRange(sprite.PixelX, 31, 47);
        Assert.InRange(sprite.PixelY, 63, 79);
    }

    [Fact]
    public void Particle_resize_parameters_are_forwarded_to_the_sprite()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10], ColorTint = [0x0000FF, 0, 0, 0], GrhResize = true, GrhResizeX = 12, GrhResizeY = 18 }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);

        var sprite = Assert.Single(new ParticleRenderService().Render(groups.Groups, catalog, 0f));

        Assert.Equal(12, sprite.Width);
        Assert.Equal(18, sprite.Height);
    }

    [Fact]
    public void Particle_physical_ranges_affect_position_over_time()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10], ColorTint = [0x0000FF, 0, 0, 0], X1 = 20, X2 = 20, VecX1 = 2, VecX2 = 2, Friction = 1 }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);

        var atStart = Assert.Single(new ParticleRenderService().Render(groups.Groups, catalog, 0f));
        var later = Assert.Single(new ParticleRenderService().Render(groups.Groups, catalog, 1f));

        Assert.True(later.PixelX > atStart.PixelX);
    }

    [Fact]
    public void Particle_runtime_life_restarts_a_particle_after_expiration()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10], ColorTint = [0x0000FF], X1 = 20, X2 = 20, Life1 = 1, Life2 = 1 }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);
        var service = new ParticleRenderService();

        var first = Assert.Single(service.Render(groups.Groups, catalog, 0f));
        var later = Assert.Single(service.Render(groups.Groups, catalog, 1f));

        Assert.Equal(first.PixelX, later.PixelX);
    }

    [Fact]
    public void Particle_runtime_applies_move_ranges_on_discrete_ticks()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10], ColorTint = [0x0000FF], X1 = 16, X2 = 16,
                XMove = 1, MoveX1 = 2, MoveX2 = 2, Friction = 1 }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);
        var service = new ParticleRenderService();

        var start = Assert.Single(service.Render(groups.Groups, catalog, 0f));
        var later = Assert.Single(service.Render(groups.Groups, catalog, 1f / 60f));

        Assert.Equal(start.PixelX + 2, later.PixelX);
    }

    [Fact]
    public void Particle_runtime_uses_vector_before_move_override_on_first_frame()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10], ColorTint = [0x0000FF], X1 = 16, X2 = 16,
                VecX1 = 1, VecX2 = 1, XMove = 1, MoveX1 = 7, MoveX2 = 7, Friction = 1 }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);
        var service = new ParticleRenderService();

        var first = Assert.Single(service.Render(groups.Groups, catalog, 0f));
        var second = Assert.Single(service.Render(groups.Groups, catalog, 1f / 60f));

        Assert.Equal(first.PixelX + 7, second.PixelX);
    }

    [Fact]
    public void Particle_runtime_applies_bounce_before_integrating_position()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10], ColorTint = [0x0000FF],
                X1 = 16, X2 = 16, Y1 = 17, Y2 = 17, VecY1 = -2, VecY2 = -2,
                Gravity = 1, GravityStrength = 1, BounceStrength = -4, Friction = 1 }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);
        var service = new ParticleRenderService();

        var first = Assert.Single(service.Render(groups.Groups, catalog, 0f));
        var second = Assert.Single(service.Render(groups.Groups, catalog, 1f / 60f));

        Assert.Equal(first.PixelY - 4, second.PixelY);
    }

    [Fact]
    public void Particle_runtime_advances_spin_angle()
    {
        var catalog = new List<ParticleData>
        {
            new() { NumOfParticles = 1, Grhs = [10], ColorTint = [0x0000FF], Spin = 1, SpinSpeedLow = 60, SpinSpeedHigh = 60 }
        };
        var groups = new ParticleGroupService();
        groups.Create(1, 1, 1);
        var service = new ParticleRenderService();

        _ = service.Render(groups.Groups, catalog, 0f);
        var later = Assert.Single(service.Render(groups.Groups, catalog, 1f / 60f));

        Assert.Equal(0.6f, later.Angle, precision: 3);
    }
}

public sealed class TextRenderTests
{
    [Fact]
    public void Font_atlas_and_text_layout_produce_glyph_quads()
    {
        var service = new TextRenderService();
        var index = service.Create("ui", "Consolas", 14);
        Assert.Equal(1, index);
        Assert.True(service.Check(1));
        Assert.False(service.Check(2));
        Assert.Equal(1, service.Find("ui"));

        var quads = service.Render("ui", "AO", 10, 20);
        Assert.Equal(2, quads.Count);
        Assert.Equal(10, quads[0].X);
        Assert.True(quads[1].X > quads[0].X);
        Assert.All(quads, quad => Assert.True(quad.U1 > quad.U0 && quad.V1 > quad.V0));
    }

    [Fact]
    public void Text_render_handles_multiline_and_alignment()
    {
        var service = new TextRenderService();
        service.Create("ui", "Consolas", 14);
        var lines = service.Render("ui", "a\nb", 0, 0);
        Assert.Contains(lines, quad => quad.Y > 0);

        var left = service.Render("ui", "abc", 100, 0);
        var right = service.Render("ui", "abc", 100, 0, 0xFFFFFF, 1f, TextAlign.Right);
        var center = service.Render("ui", "abc", 100, 0, 0xFFFFFF, 1f, TextAlign.Center);
        Assert.True(right[0].X < left[0].X);
        Assert.True(center[0].X < left[0].X);
        Assert.True(center[0].X > right[0].X);
    }
}
