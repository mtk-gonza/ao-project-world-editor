using System.Drawing;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>Full-map image export equivalent to FrmRender/MapCapture.</summary>
internal sealed class MapImageCaptureService
{
    public Bitmap Capture(WorldSector map, IReadOnlyList<GraphicData> graphics, string resourceRoot, double elapsedSeconds = 0d,
        IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<ParticleData>? particles = null,
        IReadOnlyList<NpcData>? npcs = null, IReadOnlyList<DirectionalAnimation>? bodies = null,
        IReadOnlyList<HeadIndexData>? heads = null,
        IReadOnlyList<DirectionalAnimation>? helmets = null,
        IReadOnlyList<DirectionalAnimation>? weapons = null,
        IReadOnlyList<DirectionalAnimation>? shields = null)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(graphics);
        var resolver = new PreviewResolver(resourceRoot);
        var image = new Bitmap(WorldSector.Width * 32, WorldSector.Height * 32, PixelFormat.Format32bppArgb);
        using var canvas = Graphics.FromImage(image);
        canvas.Clear(Color.Black);
        var minX = Math.Clamp((int)map.CsmSectorSize.XMin, 1, WorldSector.Width);
        var maxX = Math.Clamp((int)map.CsmSectorSize.XMax, 1, WorldSector.Width);
        var minY = Math.Clamp((int)map.CsmSectorSize.YMin, 1, WorldSector.Height);
        var maxY = Math.Clamp((int)map.CsmSectorSize.YMax, 1, WorldSector.Height);
        DrawLayer(0);
        DrawLayer(1);
        DrawObjects();
        DrawCharacters();
        DrawLayer(2);
        DrawParticles();
        DrawLayer(3);
        return image;

        void DrawLayer(int layer)
        {
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
                DrawGraphic(map.Blocks[x, y].Graphics[layer].GraphicIndex, x, y, layer);
        }

        void DrawObjects()
        {
            if (objects is null) return;
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            {
                var objectIndex = map.Blocks[x, y].Object.Index;
                var objectGraphic = map.Blocks[x, y].ObjectGraphic.GraphicIndex;
                if (objectGraphic <= 0 && objectIndex > 0 && objectIndex < objects.Count)
                    objectGraphic = objects[objectIndex].GraphicIndex;
                if (objectGraphic > 0) DrawGraphic(objectGraphic, x, y, 2);
            }
        }

        void DrawParticles()
        {
            if (particles is null) return;
            var groups = new ParticleGroupService();
            groups.SyncFromMap(map);
            foreach (var sprite in new ParticleRenderService().Render(groups.Groups, particles, (float)elapsedSeconds))
                DrawParticle(sprite);
        }

        void DrawCharacters()
        {
            if (npcs is null || bodies is null || heads is null) return;
            var characters = new CharacterListService();
            characters.RefreshAll(map, npcs);
            foreach (var sprite in new CharacterRenderService().Render(characters.Characters, bodies, heads,
                helmets, weapons, shields))
                DrawCharacter(sprite);
        }

        void DrawGraphic(int graphicIndex, int tileX, int tileY, int graphicLayer)
        {
            var resolvedIndex = ResolveFrame(graphicIndex, graphics, elapsedSeconds);
            if (resolvedIndex <= 0 || resolvedIndex >= graphics.Count || !resolver.TryGet(graphics[resolvedIndex], out var source)) return;
            using var texture = new Bitmap(source.Path);
            texture.MakeTransparent(Color.Black);
            var left = (tileX - 1) * 32;
            var top = (tileY - 1) * 32;
            if (graphicLayer is 1 or 2 or 3)
            {
                var graphic = graphics[resolvedIndex];
                var tileWidth = graphic.TileWidth <= 0 ? 1f : graphic.TileWidth;
                var tileHeight = graphic.TileHeight <= 0 ? 1f : graphic.TileHeight;
                left += 16 - (int)MathF.Floor(tileWidth * 16f);
                top += 32 - (int)MathF.Floor(tileHeight * 32f);
            }
            canvas.DrawImage(texture, new Rectangle(left, top, source.Width, source.Height),
                new Rectangle(source.X, source.Y, source.Width, source.Height), GraphicsUnit.Pixel);
        }

        void DrawParticle(RenderSprite sprite)
        {
            var resolvedIndex = ResolveFrame(sprite.GraphicIndex, graphics, elapsedSeconds);
            if (resolvedIndex <= 0 || resolvedIndex >= graphics.Count || !resolver.TryGet(graphics[resolvedIndex], out var source)) return;
            using var texture = new Bitmap(source.Path);
            texture.MakeTransparent(Color.Black);
            var width = sprite.Width > 0 ? sprite.Width : source.Width;
            var height = sprite.Height > 0 ? sprite.Height : source.Height;
            canvas.DrawImage(texture, new Rectangle(sprite.PixelX, sprite.PixelY, width, height),
                new Rectangle(source.X, source.Y, source.Width, source.Height), GraphicsUnit.Pixel);
        }

        void DrawCharacter(RenderSprite sprite)
        {
            var resolvedIndex = ResolveFrame(sprite.GraphicIndex, graphics, elapsedSeconds);
            if (resolvedIndex <= 0 || resolvedIndex >= graphics.Count || !resolver.TryGet(graphics[resolvedIndex], out var source)) return;
            using var texture = new Bitmap(source.Path);
            texture.MakeTransparent(Color.Black);
            var graphic = graphics[resolvedIndex];
            var tileWidth = graphic.TileWidth <= 0 ? 1f : graphic.TileWidth;
            var tileHeight = graphic.TileHeight <= 0 ? 1f : graphic.TileHeight;
            var left = sprite.PixelX + 16 - (int)MathF.Floor(tileWidth * 16f);
            var top = sprite.PixelY + 32 - (int)MathF.Floor(tileHeight * 32f);
            var width = sprite.Width > 0 ? sprite.Width : source.Width;
            var height = sprite.Height > 0 ? sprite.Height : source.Height;
            canvas.DrawImage(texture, new Rectangle(left, top, width, height),
                new Rectangle(source.X, source.Y, source.Width, source.Height), GraphicsUnit.Pixel);
        }
    }

    private static int ResolveFrame(int index, IReadOnlyList<GraphicData> graphics, double elapsedSeconds)
    {
        var current = index;
        for (var depth = 0; depth < 8 && current > 0 && current < graphics.Count; depth++)
        {
            var graphic = graphics[current];
            if (graphic.FileNumber > 0 || graphic.Frames.Length == 0) return current;
            var frame = graphic.Frames.Length == 1 || graphic.Speed <= 0
                ? 0
                : (int)(elapsedSeconds * 1000d * graphic.Frames.Length / graphic.Speed) % graphic.Frames.Length;
            if (frame < 0) frame += graphic.Frames.Length;
            current = graphic.Frames[frame];
        }
        return current;
    }

    public void SavePng(WorldSector map, IReadOnlyList<GraphicData> graphics, string resourceRoot, string path,
        double elapsedSeconds = 0d, IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<ParticleData>? particles = null,
        IReadOnlyList<NpcData>? npcs = null, IReadOnlyList<DirectionalAnimation>? bodies = null,
        IReadOnlyList<HeadIndexData>? heads = null,
        IReadOnlyList<DirectionalAnimation>? helmets = null,
        IReadOnlyList<DirectionalAnimation>? weapons = null,
        IReadOnlyList<DirectionalAnimation>? shields = null)
    {
        using var image = Capture(map, graphics, resourceRoot, elapsedSeconds, objects, particles, npcs, bodies, heads,
            helmets, weapons, shields);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        image.Save(path, ImageFormat.Png);
    }

    public void Save(WorldSector map, IReadOnlyList<GraphicData> graphics, string resourceRoot, string path,
        double elapsedSeconds = 0d, IReadOnlyList<ObjectData>? objects = null, IReadOnlyList<ParticleData>? particles = null,
        IReadOnlyList<NpcData>? npcs = null, IReadOnlyList<DirectionalAnimation>? bodies = null,
        IReadOnlyList<HeadIndexData>? heads = null)
    {
        using var image = Capture(map, graphics, resourceRoot, elapsedSeconds, objects, particles, npcs, bodies, heads);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".bmp" => ImageFormat.Bmp,
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            _ => ImageFormat.Png
        };
        image.Save(path, format);
    }

    private sealed class PreviewResolver
    {
        private readonly GraphicSourceResolver _resolver;
        public PreviewResolver(string root) => _resolver = new GraphicSourceResolver(root);
        public bool TryGet(GraphicData graphic, out (string Path, int X, int Y, int Width, int Height) source)
        {
            source = default;
            if (graphic.FileNumber <= 0 || graphic.PixelWidth <= 0 || graphic.PixelHeight <= 0) return false;
            try
            {
                var value = _resolver.Resolve(graphic);
                source = (value.Path, value.SourceX, value.SourceY, value.Width, value.Height);
                return true;
            }
            catch (FileNotFoundException) { return false; }
        }
    }
}
