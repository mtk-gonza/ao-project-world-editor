namespace AoProject.WorldEditor.Engine.Rendering;

internal readonly record struct TextQuad(int X, int Y, int Width, int Height, float U0, float V0, float U1, float V1, int Color);

internal enum TextAlign { Left, Center, Right }

/// <summary>
/// Port of clsDX8Engine.Font_Make / Font_Create / Font_Next_Open / Font_Check /
/// Text_Render / Text_Render_ext: layout of glyph quads for a font atlas.
/// </summary>
internal sealed class TextRenderService
{
    private readonly Dictionary<string, FontAtlas> _fonts = [];

    public int NextOpen => _fonts.Count + 1;
    public int Count => _fonts.Count;

    public bool Check(int index) => index >= 1 && index <= _fonts.Count;

    public int Create(string key, string family, int size, FontStyle style = FontStyle.Regular)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_fonts.ContainsKey(key)) return Find(key);
        _fonts[key] = FontAtlas.Build(family, size, style);
        return _fonts.Count;
    }

    public int Find(string key) => _fonts.Keys.ToList().IndexOf(key) + 1;

    public FontAtlas Atlas(string key) => _fonts[key];

    public IReadOnlyList<TextQuad> Render(string key, string text, int x, int y, int color = 0xFFFFFF,
        float scale = 1f, TextAlign align = TextAlign.Left, int maxWidth = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(text);
        if (!_fonts.TryGetValue(key, out var atlas)) throw new KeyNotFoundException(key);

        var lines = text.Replace("\r", string.Empty).Split('\n');
        var quads = new List<TextQuad>();
        var lineY = y;
        foreach (var line in lines)
        {
            var lineWidth = 0;
            foreach (var character in line) lineWidth += (int)Math.Ceiling(atlas.Glyph(character).Advance * scale);
            var cursorX = align switch
            {
                TextAlign.Center => x + (maxWidth > 0 ? (maxWidth - lineWidth) / 2 : -lineWidth / 2),
                TextAlign.Right => x - lineWidth,
                _ => x
            };
            foreach (var character in line)
            {
                var glyph = atlas.Glyph(character);
                if (character != ' ')
                {
                    var width = (int)Math.Ceiling(glyph.Width * scale);
                    var height = (int)Math.Ceiling(glyph.Height * scale);
                    quads.Add(new TextQuad(
                        cursorX, lineY, width, height,
                        glyph.X / (float)atlas.Width, glyph.Y / (float)atlas.Height,
                        (glyph.X + glyph.Width) / (float)atlas.Width, (glyph.Y + glyph.Height) / (float)atlas.Height,
                        color));
                }
                cursorX += (int)Math.Ceiling(glyph.Advance * scale);
            }
            lineY += (int)Math.Ceiling(atlas.LineHeight * scale);
        }
        return quads;
    }
}
