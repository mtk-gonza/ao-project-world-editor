using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class ParticleGroupAndLightExtraTests
{
    [Fact]
    public void Particle_groups_support_the_reference_bookkeeping()
    {
        var groups = new ParticleGroupService();
        var group = groups.Create(4, 5, 12);
        Assert.True(group.Active); Assert.True(groups.Check(1)); Assert.Equal(1, groups.Find(4, 5));
        Assert.True(groups.Make(1, 34)); Assert.Equal(34, group.Type);
        Assert.Equal(1, groups.Destroy(1)); Assert.False(groups.Check(1));
        Assert.Equal(1, groups.NextOpen());
        groups.Create(6, 7, 1);
        Assert.Equal(1, groups.RemoveAll());
    }

    [Fact]
    public void Particle_groups_sync_from_map_blocks()
    {
        var map = new WorldMap(); map.Blocks[8, 9].ParticleIndex = 5;
        var groups = new ParticleGroupService();
        Assert.Equal(1, groups.SyncFromMap(map));
        Assert.Equal(1, groups.Find(8, 9));
    }

    [Fact]
    public void Particle_group_id_is_preserved_and_shared_tiles_are_not_duplicated()
    {
        var map = new WorldMap();
        map.Blocks[8, 9].ParticleGroup = 7;
        map.Blocks[8, 9].ParticleIndex = 5;
        map.Blocks[9, 9].ParticleGroup = 7;
        map.Blocks[9, 9].ParticleIndex = 5;
        var groups = new ParticleGroupService();

        Assert.Equal(1, groups.SyncFromMap(map));
        Assert.True(groups.Check(7));
        Assert.Equal(7, groups.Groups.Single(group => group.Active).Index);
        Assert.Equal(8, groups.Groups.Single(group => group.Active).X);
    }

    [Fact]
    public void Sync_from_map_preserves_the_runtime_group_instance()
    {
        var map = new WorldMap();
        map.Blocks[8, 9].ParticleGroup = 4;
        map.Blocks[8, 9].ParticleIndex = 5;
        var groups = new ParticleGroupService();
        groups.SyncFromMap(map);
        var first = groups.Groups[3];

        groups.SyncFromMap(map);

        Assert.Same(first, groups.Groups[3]);
    }

    [Fact]
    public void Lights_move_by_heading_and_render_only_active_ranged_lights()
    {
        var lights = new LightListService();
        lights.Create(10, 10, 8, 0xFFFFFF);
        lights.MoveByHead(1, (byte)Heading.South);
        Assert.Equal(1, lights.Find(10, 11));
        Assert.Single(lights.Renderable());
        lights.Destroy(1);
        Assert.Empty(lights.Renderable());
        Assert.Equal(1, lights.Erase(1));
    }
}
