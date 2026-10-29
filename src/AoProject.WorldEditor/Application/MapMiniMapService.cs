using System.Drawing;
using System.Drawing.Imaging;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Minimap toggles equivalent to the VB6 MMiniMap_* flags. The defaults match
/// the stock WorldEditor configuration: layers 1 and 2 on, everything else off
/// (modGeneral.bas:556-557).
/// </summary>
internal readonly record struct MapMiniMapOptions(
    bool ShowLayer1 = true,
    bool ShowLayer2 = true,
    bool ShowLayer3 = false,
    bool ShowLayer4 = false,
    bool ShowBlocked = false,
    bool ShowObjects = false,
    bool ShowNpcs = false,
    bool ShowParticles = false)
{
    /// <summary>
    /// Stock WorldEditor defaults. Use this instead of <c>new MapMiniMapOptions()</c>:
    /// for a struct that expression binds to the zeroed parameterless constructor.
    /// </summary>
    public static MapMiniMapOptions Vb6Default { get; } = new(true, true);
}

/// <summary>Small minimap export equivalent to frmMain minimapSave/SaveAllMiniMap.</summary>
internal sealed class MapMiniMapService
{
    public Bitmap Create(WorldMap map, IReadOnlyList<GraphicData> graphics, bool showBlocked)
        => Create(map, graphics, MapMiniMapOptions.Vb6Default with { ShowBlocked = showBlocked });

    public Bitmap Create(WorldMap map, IReadOnlyList<GraphicData> graphics, MapMiniMapOptions options,
        IReadOnlyList<ObjectData>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(graphics);
        var image = new Bitmap(WorldMap.Width, WorldMap.Height, PixelFormat.Format32bppArgb);
        using (var canvas = Graphics.FromImage(image)) canvas.Clear(Color.Black);
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var block = map.Blocks[x, y];
            var color = Color.Black;
            for (var layer = 0; layer < 4; layer++)
            {
                if (!LayerVisible(options, layer)) continue;
                var index = block.Graphics[layer].GraphicIndex;
                if (index > 0 && index < graphics.Count && graphics[index].MiniMapColor != 0)
                    color = FromColorRef(graphics[index].MiniMapColor);
            }
            if (options.ShowNpcs && block.NpcIndex > 0) color = Color.Yellow;
            if (options.ShowObjects)
            {
                var objectGraphic = block.ObjectGraphic.GraphicIndex;
                if (objectGraphic <= 0 && objects is not null && block.Object.Index > 0 && block.Object.Index < objects.Count)
                    objectGraphic = objects[block.Object.Index].GraphicIndex;
                if (objectGraphic > 0 && objectGraphic < graphics.Count && graphics[objectGraphic].MiniMapColor != 0)
                    color = FromColorRef(graphics[objectGraphic].MiniMapColor);
            }
            if (options.ShowBlocked && block.Blocked > 0) color = Color.Red;
            if (options.ShowParticles && block.ParticleIndex > 0) color = Color.White;
            image.SetPixel(x - 1, y - 1, color);
        }
        return image;
    }

    private static bool LayerVisible(MapMiniMapOptions options, int layer) => layer switch
    {
        0 => options.ShowLayer1,
        1 => options.ShowLayer2,
        2 => options.ShowLayer3,
        3 => options.ShowLayer4,
        _ => false
    };

    /// <summary>
    /// The "void" ground (graphic 1) has palette 0x00010101: almost black but not
    /// zero, so equality checks missed it and the minimap painted it black.
    /// </summary>
    private static bool IsNearlyBlack(Color color) =>
        color.A == 0 || (color.R <= 8 && color.G <= 8 && color.B <= 8);

    internal static Color FromColorRef(int colorRef)
    {
        var raw = unchecked((uint)colorRef);
        // minimap.bin stores the resource palette as 0x00RRGGBB. The legacy
        // name is historical; reversing it here turns water into red/brown.
        return Color.FromArgb(255, (int)((raw >> 16) & 0xFF), (int)((raw >> 8) & 0xFF), (int)(raw & 0xFF));
    }

    public void SavePng(WorldMap map, IReadOnlyList<GraphicData> graphics, bool showBlocked, string path)
        => SavePng(map, graphics, MapMiniMapOptions.Vb6Default with { ShowBlocked = showBlocked }, null, path);

    /// <summary>
    /// Smooth minimap at 1 px per tile using the average colour of each tile's art.
    /// At 2 px/tile (the editor panel) this reads as terrain without the aliasing
    /// (stripes) that compressing the raw art produces.
    /// </summary>
    public Bitmap CreateSmooth(WorldMap map, IReadOnlyList<GraphicData> graphics,
        GraphicAverageColorService averages, MapMiniMapOptions? options = null,
        IReadOnlyList<ObjectData>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(averages);
        var chosen = options ?? new MapMiniMapOptions(ShowNpcs: true);
        var image = new Bitmap(WorldMap.Width, WorldMap.Height, PixelFormat.Format32bppArgb);
        using var canvas = Graphics.FromImage(image);
        canvas.Clear(Color.Black);
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var block = map.Blocks[x, y];
            var color = Color.Black;
            for (var layer = 0; layer < 4; layer++)
            {
                if (!LayerVisible(chosen, layer)) continue;
                var index = block.Graphics[layer].GraphicIndex;
                if (index <= 0 || index >= graphics.Count) continue;
                var data = graphics[index];
                var argb = averages.Get(data);
                if (argb == 0 && data.MiniMapColor != 0) argb = FromColorRef(data.MiniMapColor).ToArgb();
                // Animations have no file of their own (FileNumber = 0): use the
                // average of their first frame instead of leaving the tile black.
                if (argb == 0 && data.Frames.Length > 1)
                {
                    var frame = data.Frames[0];
                    if (frame > 0 && frame < graphics.Count) argb = averages.Get(graphics[frame]);
                }
                if (argb != 0) color = Color.FromArgb(argb);
            }
            // Some maps leave layer 1 empty or with the black "void" graphic
            // (graphic 1 has palette 0x00010101, i.e. almost black but not zero)
            // and the visible terrain lives on layer 2/3: an island would be
            // painted black. Fall back to the first upper layer with a real
            // colour (never layer 4: roofs would hide the ground under them).
            if (IsNearlyBlack(color))
            {
                for (var layer = 1; layer <= 2; layer++)
                {
                    var index = block.Graphics[layer].GraphicIndex;
                    if (index <= 0 || index >= graphics.Count) continue;
                    var data = graphics[index];
                    var argb = averages.Get(data);
                    if (argb == 0 && data.MiniMapColor != 0) argb = FromColorRef(data.MiniMapColor).ToArgb();
                    if (argb == 0 && data.Frames.Length > 1)
                    {
                        var frame = data.Frames[0];
                        if (frame > 0 && frame < graphics.Count) argb = averages.Get(graphics[frame]);
                    }
                    if (argb == 0) continue;
                    var candidate = Color.FromArgb(argb);
                    if (IsNearlyBlack(candidate)) continue;
                    color = candidate;
                    break;
                }
            }
            if (chosen.ShowNpcs && block.NpcIndex > 0) color = Color.Yellow;
            if (chosen.ShowObjects)
            {
                var objectGraphic = block.ObjectGraphic.GraphicIndex;
                if (objectGraphic <= 0 && objects is not null && block.Object.Index > 0 &&
                    block.Object.Index < objects.Count)
                    objectGraphic = objects[block.Object.Index].GraphicIndex;
                if (objectGraphic > 0 && objectGraphic < graphics.Count &&
                    graphics[objectGraphic].MiniMapColor != 0)
                    color = FromColorRef(graphics[objectGraphic].MiniMapColor);
            }
            if (chosen.ShowBlocked && block.Blocked > 0) color = Color.Red;
            if (chosen.ShowParticles && block.ParticleIndex > 0) color = Color.White;
            image.SetPixel(x - 1, y - 1, color);
        }
        return image;
    }

    /// <summary>
    /// Detailed minimap: a real miniature of the tile art (nearest-neighbour) at
    /// <paramref name="scale"/> pixels per tile. Meant for a single map panel;
    /// at world scale the panel is sub-pixel per tile, so the flat colour map is
    /// still the right representation.
    /// </summary>
    public Bitmap CreateDetailed(WorldMap map, IReadOnlyList<GraphicData> graphics, string resourceRoot,
        int scale = 2, MapMiniMapOptions? options = null, IReadOnlyList<ObjectData>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(graphics);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        scale = Math.Clamp(scale, 1, 8);
        var chosen = options ?? MapMiniMapOptions.Vb6Default;
        var image = new Bitmap(WorldMap.Width * scale, WorldMap.Height * scale, PixelFormat.Format32bppArgb);
        using var canvas = Graphics.FromImage(image);
        canvas.Clear(Color.Black);
        // Shrinking 32 px of tile art to 2-8 px with NearestNeighbor aliases waves
        // and grass into stripes. A high quality filter averages instead.
        canvas.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        canvas.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        canvas.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

        GraphicSourceResolver? resolver = null;
        var textures = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        // Mirror the sampler at tile edges so the bicubic filter does not blend
        // each tile with the black background (that produced a visible grid).
        using var tileAttributes = new System.Drawing.Imaging.ImageAttributes();
        tileAttributes.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        try
        {
            resolver = new GraphicSourceResolver(resourceRoot);
        }
        catch (DirectoryNotFoundException)
        {
            // No sprites available: fall back to the flat palette below.
        }
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        try
        {
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            {
                var block = map.Blocks[x, y];
                var fallback = Color.Black;
                for (var layer = 0; layer < 4; layer++)
                {
                    if (!LayerVisible(chosen, layer)) continue;
                    var index = block.Graphics[layer].GraphicIndex;
                    if (index <= 0 || index >= graphics.Count) continue;
                    var data = graphics[index];
                    if (!data.Active) continue;
                    if (data.MiniMapColor != 0) fallback = FromColorRef(data.MiniMapColor);
                    if (resolver is null || data.FileNumber <= 0) continue;
                    if (!textures.TryGetValue(data.FileNumber.ToString(), out var texture))
                    {
                        try
                        {
                            var source = resolver.Resolve(data);
                            var loaded = new Bitmap(source.Path);
                            loaded.MakeTransparent(Color.Black);
                            textures[data.FileNumber.ToString()] = loaded;
                            texture = loaded;
                        }
                        catch (Exception error) when (error is FileNotFoundException or ArgumentException)
                        {
                            textures[data.FileNumber.ToString()] = null!;
                            continue;
                        }
                    }
                    if (texture is null) continue;
                    canvas.DrawImage(texture,
                        new Rectangle((x - 1) * scale, (y - 1) * scale, scale, scale),
                        data.SourceX, data.SourceY, data.PixelWidth, data.PixelHeight,
                        GraphicsUnit.Pixel, tileAttributes);
                    fallback = Color.Black;
                }
                if (fallback != Color.Black)
                    using (var brush = new SolidBrush(fallback))
                        canvas.FillRectangle(brush, (x - 1) * scale, (y - 1) * scale, scale, scale);

                // Semantic markers over the real art. At 2 px per tile a sprite is
                // unreadable, but a coloured dot still tells where the entities are
                // (same colours and precedence as the legacy minimap).
                var marker = Color.Empty;
                if (chosen.ShowNpcs && block.NpcIndex > 0) marker = Color.Yellow;
                if (chosen.ShowObjects)
                {
                    var objectGraphic = block.ObjectGraphic.GraphicIndex;
                    if (objectGraphic <= 0 && objects is not null && block.Object.Index > 0 &&
                        block.Object.Index < objects.Count)
                        objectGraphic = objects[block.Object.Index].GraphicIndex;
                    if (objectGraphic > 0 && objectGraphic < graphics.Count &&
                        graphics[objectGraphic].MiniMapColor != 0)
                        marker = FromColorRef(graphics[objectGraphic].MiniMapColor);
                }
                if (chosen.ShowBlocked && block.Blocked > 0) marker = Color.Red;
                if (chosen.ShowParticles && block.ParticleIndex > 0) marker = Color.White;
                if (marker != Color.Empty)
                    using (var brush = new SolidBrush(marker))
                        canvas.FillRectangle(brush, (x - 1) * scale, (y - 1) * scale, scale, scale);
            }
        }
        finally
        {
            foreach (var texture in textures.Values) texture?.Dispose();
        }
        return image;
    }

    public void SaveDetailedPng(WorldMap map, IReadOnlyList<GraphicData> graphics, string resourceRoot,
        string path, int scale = 2, MapMiniMapOptions? options = null)
    {
        using var image = CreateDetailed(map, graphics, resourceRoot, scale, options);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        image.Save(path, ImageFormat.Png);
    }

    public void SavePng(WorldMap map, IReadOnlyList<GraphicData> graphics, MapMiniMapOptions options,
        IReadOnlyList<ObjectData>? objects, string path)
    {
        using var image = Create(map, graphics, options, objects);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        image.Save(path, ImageFormat.Png);
    }

    /// <summary>
    /// Writes the minimap in the format the client loads
    /// (<c>Recursos/Minimapas/mapaN.bmp</c>): 100x100, 16bpp BI_RGB. The header
    /// replicates VB6 <c>SavePicture</c>, including its two extra bytes in
    /// biSizeImage and the 2834 pixels-per-meter fields.
    /// </summary>
    public void SaveBmp(WorldMap map, IReadOnlyList<GraphicData> graphics, MapMiniMapOptions options,
        IReadOnlyList<ObjectData>? objects, string path)
    {
        using var image = Create(map, graphics, options, objects);
        WriteBmp(image, path);
    }

    internal static void WriteBmp(Bitmap image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        var width = image.Width;
        var height = image.Height;
        var rowBytes = (width * 2 + 3) / 4 * 4;
        var sizeImage = rowBytes * height + 2;
        var fileSize = 54 + sizeImage;
        var bytes = new byte[fileSize];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, fileSize);
        WriteInt32(bytes, 10, 54);
        WriteInt32(bytes, 14, 40);
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 22, height);
        WriteInt16(bytes, 26, 1);
        WriteInt16(bytes, 28, 16);
        WriteInt32(bytes, 30, 0);
        WriteInt32(bytes, 34, sizeImage);
        WriteInt32(bytes, 38, 2834);
        WriteInt32(bytes, 42, 2834);

        // BMP rows are bottom-up. Convert 32bpp ARGB to RGB555 the same way the
        // legacy 16bpp save quantizes the channel values.
        var data = image.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var source = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, source, 0, source.Length);
            for (var y = 0; y < height; y++)
            {
                var row = 54 + (height - 1 - y) * rowBytes;
                var line = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var pixel = line + x * 4;
                    var red = source[pixel + 2] >> 3;
                    var green = source[pixel + 1] >> 3;
                    var blue = source[pixel] >> 3;
                    var value = (ushort)((red << 10) | (green << 5) | blue);
                    bytes[row + x * 2] = (byte)(value & 0xFF);
                    bytes[row + x * 2 + 1] = (byte)(value >> 8);
                }
            }
        }
        finally
        {
            image.UnlockBits(data);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt16(byte[] target, int offset, short value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }
}
