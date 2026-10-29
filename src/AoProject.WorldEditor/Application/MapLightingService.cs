using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Port of the VB6 two-pass light model:
/// flat lights for stored range &lt; 100 (clsDX8Engine.Light_Render) and round
/// lights for stored range &gt;= 100 with effective range = stored - 99
/// (clsLight.LightCalculate). Corners with no light fall back to the ambient
/// colour (Device_Box_Textured_Render).
/// </summary>
internal sealed class MapLightingService
{
    public const int DayLight = unchecked((int)0xFFFFFFFF);
    public const int NightLight = unchecked((int)0xFF8080AA);

    public static int AmbientFromColor(int colorAmb)
    {
        if (colorAmb == 0) return DayLight;
        return (colorAmb & unchecked((int)0xFF000000)) == 0 ? NormalizeAmbientColor(colorAmb) : colorAmb;
    }

    /// <summary>
    /// VB6 ambient (BaseLight) decode: <c>clsLight.LightRender</c> reads the low
    /// byte as red, so a stored 0x00RRGGBB ends up with red and blue swapped.
    /// Replicated on purpose.
    /// </summary>
    public static int NormalizeAmbientColor(int rgb)
    {
        var r = rgb & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = (rgb >> 16) & 0xFF;
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }

    /// <summary>
    /// Per-tile light decode: the CSM stores 0x00RRGGBB and the VB6 loader
    /// passes the high byte as red (ModCargaIAO.bas:353-358). Unlike the ambient,
    /// this one is not swapped.
    /// </summary>
    public static int LightColorFromMap(int rgb)
    {
        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }

    /// <summary>Legacy helper kept for callers that already have an RGB long.</summary>
    public static int NormalizeLightColor(int rgb) => LightColorFromMap(rgb);

    /// <summary>Returns [x, y, corner] ARGB colours for the whole map.</summary>
    /// <param name="includeFlatLights">
    /// VB6 only renders flat lights (stored range &lt; 100) after a light edit;
    /// map load only runs the round-light pass (`LightA.Create_Light_To_Map`).
    /// Keep this false to reproduce the load-time appearance.
    /// </param>
    public int[,,] Compute(WorldMap map, int ambientColor, bool includeFlatLights = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        var lights = new int[WorldMap.Width + 1, WorldMap.Height + 1, 4];
        var minX = Math.Clamp((int)map.CsmMapSize.XMin, 1, WorldMap.Width);
        var maxX = Math.Clamp((int)map.CsmMapSize.XMax, 1, WorldMap.Width);
        var minY = Math.Clamp((int)map.CsmMapSize.YMin, 1, WorldMap.Height);
        var maxY = Math.Clamp((int)map.CsmMapSize.YMax, 1, WorldMap.Height);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var block = map.Blocks[x, y];
            var range = block.Light.Range;
            if (range == 0) continue;
            if (range < 100)
            {
                if (includeFlatLights) ApplyFlatLight(lights, x, y, range, LightColorFromMap(block.Light.Color));
            }
            else ApplyRoundLight(lights, x, y, range - 99, LightColorFromMap(block.Light.Color), ambientColor);
        }

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        for (var corner = 0; corner < 4; corner++)
            if (lights[x, y, corner] == 0) lights[x, y, corner] = ambientColor;
        return lights;
    }

    private static void ApplyFlatLight(int[,,] lights, int centerX, int centerY, int range, int color)
    {
        // VB6 keeps the raw square and applies InMapBounds to every corner
        // and border tile. Clamping the square first changes corner ownership
        // for lights placed near the map edge.
        var minX = centerX - range;
        var maxX = centerX + range;
        var minY = centerY - range;
        var maxY = centerY + range;

        SetCorners(centerX, centerY, 0, 1, 2, 3);
        SetCorners(minX, minY, 2);
        SetCorners(maxX, minY, 0);
        SetCorners(maxX, maxY, 1);
        SetCorners(minX, maxY, 3);

        for (var x = minX + 1; x <= maxX - 1; x++)
        {
            SetCorners(x, minY, 0, 2);
            SetCorners(x, maxY, 1, 3);
        }
        for (var y = minY + 1; y <= maxY - 1; y++)
        {
            SetCorners(minX, y, 2, 3);
            SetCorners(maxX, y, 0, 1);
        }
        for (var x = minX + 1; x <= maxX - 1; x++)
        for (var y = minY + 1; y <= maxY - 1; y++)
            SetCorners(x, y, 0, 1, 2, 3);

        void SetCorners(int x, int y, params int[] corners)
        {
            if (x is < 1 or > WorldMap.Width || y is < 1 or > WorldMap.Height) return;
            foreach (var corner in corners) lights[x, y, corner] = color;
        }
    }

    private static void ApplyRoundLight(int[,,] lights, int centerX, int centerY, int range, int color, int ambientColor)
    {
        if (range <= 0) return;
        var radius = range * 32f;
        var lightX = centerX * 32 + 16;
        var lightY = centerY * 32 + 16;
        var lightColor = ToColorValue(color);
        var ambient = ToColorValue(AmbientFromColor(ambientColor));
        var minX = Math.Max(1, centerX - range);
        var maxX = Math.Min(WorldMap.Width, centerX + range);
        var minY = Math.Max(1, centerY - range);
        var maxY = Math.Min(WorldMap.Height, centerY + range);

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            ApplyCorner(lights, x, y, 1, x * 32, y * 32);
            ApplyCorner(lights, x, y, 3, x * 32 + 32, y * 32);
            ApplyCorner(lights, x, y, 0, x * 32, y * 32 + 32);
            ApplyCorner(lights, x, y, 2, x * 32 + 32, y * 32 + 32);
        }

        void ApplyCorner(int[,,] table, int x, int y, int corner, int cornerX, int cornerY)
        {
            var dx = lightX - cornerX;
            var dy = lightY - cornerY;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > radius) return;
            table[x, y, corner] = Lerp(lightColor, ambient, distance / radius);
        }
    }

    private static (float R, float G, float B) ToColorValue(int argb) => (
        ((argb >> 16) & 0xFF) / 255f,
        ((argb >> 8) & 0xFF) / 255f,
        (argb & 0xFF) / 255f);

    private static int Lerp((float R, float G, float B) from, (float R, float G, float B) to, float t)
    {
        var r = (int)MathF.Round((from.R + (to.R - from.R) * t) * 255f);
        var g = (int)MathF.Round((from.G + (to.G - from.G) * t) * 255f);
        var b = (int)MathF.Round((from.B + (to.B - from.B) * t) * 255f);
        return unchecked((int)0xFF000000) | (Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255);
    }
}
