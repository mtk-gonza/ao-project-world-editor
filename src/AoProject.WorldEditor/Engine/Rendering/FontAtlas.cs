using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AoProject.WorldEditor.Engine.Rendering;

internal readonly record struct GlyphMetrics(int X, int Y, int Width, int Height, int Advance);

internal sealed class FontAtlas : IDisposable
{
    public const int FirstChar = 32;
    public const int LastChar = 126;

    private FontAtlas(byte[] pixels, int width, int height, GlyphMetrics[] glyphs, int lineHeight, int ascent)
    {
        Pixels = pixels; Width = width; Height = height; Glyphs = glyphs; LineHeight = lineHeight; Ascent = ascent;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public GlyphMetrics[] Glyphs { get; }
    public int LineHeight { get; }
    public int Ascent { get; }

    public GlyphMetrics Glyph(char character)
    {
        var index = character - FirstChar;
        return index >= 0 && index < Glyphs.Length ? Glyphs[index] : Glyphs[0];
    }

    public static FontAtlas Build(string family, int size, FontStyle style = FontStyle.Regular)
    {
        using var font = new Font(family, size, style, GraphicsUnit.Pixel);
        using var bitmap = new Bitmap(1024, 1024, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        }

        var glyphs = new GlyphMetrics[LastChar - FirstChar + 1];
        var cursorX = 1;
        var cursorY = 1;
        var lineHeight = 0;
        using var measuring = Graphics.FromImage(bitmap);
        var ascent = (int)Math.Ceiling(font.GetHeight());

        using (var graphics = Graphics.FromImage(bitmap))
        {
            for (var code = FirstChar; code <= LastChar; code++)
            {
                var character = ((char)code).ToString();
                var textSize = TextRenderer.MeasureText(measuring, character, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                var advance = Math.Max(1, textSize.Width);
                var height = Math.Max(1, textSize.Height);
                if (cursorX + advance + 1 >= bitmap.Width) { cursorX = 1; cursorY += lineHeight + 1; lineHeight = 0; }
                if (cursorY + height + 1 >= bitmap.Height) break;
                TextRenderer.DrawText(graphics, character, font, new Point(cursorX, cursorY), Color.White, Color.Transparent, TextFormatFlags.NoPadding);
                glyphs[code - FirstChar] = new GlyphMetrics(cursorX, cursorY, advance, height, advance);
                cursorX += advance + 1;
                lineHeight = Math.Max(lineHeight, height);
            }
        }

        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        try
        {
            for (var row = 0; row < bitmap.Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * bitmap.Width * 4, bitmap.Width * 4);
        }
        finally { bitmap.UnlockBits(data); }

        return new FontAtlas(pixels, bitmap.Width, bitmap.Height, glyphs, Math.Max(1, lineHeight), ascent);
    }

    public void Dispose() { }
}
