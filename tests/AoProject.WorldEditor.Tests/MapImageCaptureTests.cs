using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Xunit;

namespace AoProject.WorldEditor.Tests;

public sealed class MapImageCaptureTests
{
    [Fact]
    public void Capture_exports_a_full_100_by_100_tile_canvas()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            using var image = new MapImageCaptureService().Capture(new WorldMap(), [new GraphicData()], root);
            Assert.Equal(3200, image.Width); Assert.Equal(3200, image.Height);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Capture_resolves_an_animated_graphic_to_its_first_frame()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-capture-frame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        var path = Path.Combine(root, "Graficos", "1.png");
        using (var bitmap = new System.Drawing.Bitmap(1, 1))
        {
            bitmap.SetPixel(0, 0, System.Drawing.Color.Red);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        try
        {
            var map = new WorldMap();
            map.Blocks[1, 1].Graphics[0].GraphicIndex = 2;
            var graphics = new List<GraphicData>
            {
                new(),
                new() { FileNumber = 1, PixelWidth = 1, PixelHeight = 1 },
                new() { Frames = [1] }
            };

            using var image = new MapImageCaptureService().Capture(map, graphics, root);

            Assert.Equal(System.Drawing.Color.Red.ToArgb(), image.GetPixel(0, 0).ToArgb());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Capture_uses_elapsed_time_for_animated_frames()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-capture-time-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            using (var bitmap = new System.Drawing.Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, System.Drawing.Color.Blue);
                bitmap.Save(Path.Combine(root, "Graficos", "1.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            using (var bitmap = new System.Drawing.Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, System.Drawing.Color.Green);
                bitmap.Save(Path.Combine(root, "Graficos", "2.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            var map = new WorldMap();
            map.Blocks[1, 1].Graphics[0].GraphicIndex = 3;
            var graphics = new List<GraphicData>
            {
                new(),
                new() { FileNumber = 1, PixelWidth = 1, PixelHeight = 1 },
                new() { FileNumber = 2, PixelWidth = 1, PixelHeight = 1 },
                new() { Frames = [1, 2], Speed = 1000 }
            };

            using var image = new MapImageCaptureService().Capture(map, graphics, root, 0.6d);

            Assert.Equal(System.Drawing.Color.Green.ToArgb(), image.GetPixel(0, 0).ToArgb());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Capture_draws_object_graphic_from_object_data_when_needed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-capture-object-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            using (var bitmap = new System.Drawing.Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, System.Drawing.Color.Yellow);
                bitmap.Save(Path.Combine(root, "Graficos", "1.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            var map = new WorldMap();
            map.Blocks[1, 1].Object.Index = 1;
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData { FileNumber = 1, PixelWidth = 1, PixelHeight = 1 }
            };
            var objects = new[] { new ObjectData(), new ObjectData { GraphicIndex = 1 } };

            using var image = new MapImageCaptureService().Capture(map, graphics, root, objects: objects);

            Assert.Equal(System.Drawing.Color.Yellow.ToArgb(), image.GetPixel(0, 0).ToArgb());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Capture_includes_particle_streams_when_catalog_is_provided()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-capture-particle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            using (var bitmap = new System.Drawing.Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, System.Drawing.Color.Cyan);
                bitmap.Save(Path.Combine(root, "Graficos", "1.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            var map = new WorldMap();
            map.Blocks[1, 1].ParticleIndex = 1;
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData { FileNumber = 1, PixelWidth = 1, PixelHeight = 1 }
            };
            var particles = new[]
            {
                new ParticleData { NumOfParticles = 1, Grhs = [1], ColorTint = [0xFFFFFF, 0, 0, 0] }
            };

            using var image = new MapImageCaptureService().Capture(map, graphics, root, particles: particles);

            Assert.Equal(System.Drawing.Color.Cyan.ToArgb(), image.GetPixel(7, 7).ToArgb());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Capture_includes_npc_body_and_head_when_catalogs_are_provided()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-capture-npc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            using (var bitmap = new System.Drawing.Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, System.Drawing.Color.Magenta);
                bitmap.Save(Path.Combine(root, "Graficos", "1.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            var map = new WorldMap();
            map.Blocks[1, 1].NpcIndex = 1;
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData { FileNumber = 1, PixelWidth = 1, PixelHeight = 1 }
            };
            var body = new DirectionalAnimation();
            body.Walk[1] = new GraphicAnimation { GraphicIndex = 1 };
            var head = new HeadIndexData();
            head.Head[1] = new GraphicAnimation { GraphicIndex = 1 };
            var npcs = new[] { new NpcData(), new NpcData { Body = 1, Head = 1, Heading = 1 } };

            using var image = new MapImageCaptureService().Capture(map, graphics, root,
                npcs: npcs, bodies: new[] { new DirectionalAnimation(), body }, heads: new[] { new HeadIndexData(), head });

            Assert.Equal(System.Drawing.Color.Magenta.ToArgb(), image.GetPixel(0, 0).ToArgb());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Capture_preserves_the_legacy_map_capture_layer_order()
    {
        var root = Path.Combine(Path.GetTempPath(), "ao-capture-layers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Graficos"));
        try
        {
            using (var first = new System.Drawing.Bitmap(1, 1))
            using (var second = new System.Drawing.Bitmap(1, 1))
            {
                first.SetPixel(0, 0, System.Drawing.Color.Red);
                second.SetPixel(0, 0, System.Drawing.Color.Blue);
                first.Save(Path.Combine(root, "Graficos", "1.png"), System.Drawing.Imaging.ImageFormat.Png);
                second.Save(Path.Combine(root, "Graficos", "2.png"), System.Drawing.Imaging.ImageFormat.Png);
                using var auxiliary = new System.Drawing.Bitmap(1, 1);
                auxiliary.SetPixel(0, 0, System.Drawing.Color.Green);
                auxiliary.Save(Path.Combine(root, "Graficos", "3.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            var map = new WorldMap();
            map.Blocks[1, 1].Graphics[0].GraphicIndex = 1;
            map.Blocks[1, 1].Graphics[1].GraphicIndex = 2;
            map.Blocks[1, 1].Graphics[4].GraphicIndex = 3;
            var graphics = new[]
            {
                new GraphicData(),
                new GraphicData { FileNumber = 1, PixelWidth = 1, PixelHeight = 1 },
                new GraphicData { FileNumber = 2, PixelWidth = 1, PixelHeight = 1 },
                new GraphicData { FileNumber = 3, PixelWidth = 1, PixelHeight = 1 }
            };

            using var image = new MapImageCaptureService().Capture(map, graphics, root);

            Assert.Equal(System.Drawing.Color.Blue.ToArgb(), image.GetPixel(0, 0).ToArgb());
        }
        finally { Directory.Delete(root, true); }
    }
}
