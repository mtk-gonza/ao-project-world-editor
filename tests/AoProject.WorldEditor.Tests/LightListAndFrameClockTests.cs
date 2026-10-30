using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class LightListAndFrameClockTests
{
    [Fact]
    public void Light_list_creates_finds_moves_and_removes_lights()
    {
        var lights = new LightListService();
        var light = lights.Create(10, 11, 8, 0x112233);
        Assert.True(light.Active); Assert.Equal(1, lights.Find(10, 11));
        lights.Move(1, 20, 21);
        Assert.Equal(0, lights.Find(10, 11)); Assert.Equal(1, lights.Find(20, 21));
        Assert.True(lights.Check(1));
        Assert.Equal(1, lights.RemoveAll());
        Assert.False(lights.Check(1));
        Assert.Equal(1, lights.NextOpen());
    }

    [Fact]
    public void Light_list_syncs_from_map_blocks()
    {
        var map = new WorldSector();
        map.Blocks[3, 4].Light.Range = 9; map.Blocks[3, 4].Light.Color = 0x445566;
        var lights = new LightListService();
        Assert.Equal(1, lights.SyncFromMap(map));
        Assert.Equal(1, lights.Find(3, 4));
    }

    [Fact]
    public void Frame_clock_measures_frames_and_elapsed_time()
    {
        var clock = new FrameClock();
        Assert.Equal(1, clock.CountFrame());
        Assert.Equal(2, clock.CountFrame());
        Assert.True(clock.ElapsedSeconds >= 0);
        Assert.True(clock.FrameDeltaSeconds() >= 0);
        Assert.True(clock.FramesPerSecond(clock.ElapsedSeconds) >= 0);
    }
}
