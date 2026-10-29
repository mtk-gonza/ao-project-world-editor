using System.Globalization;

namespace AoProject.WorldEditor.Application;

internal readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    public int ToBgrLong() => Red | (Green << 8) | (Blue << 16);
    public string ToVbHex() => $"&H{Red:X2}{Green:X2}{Blue:X2}";
}

internal static class ColorCodec
{
    public static RgbColor FromBgrLong(int color) => new((byte)(color & 0xFF), (byte)((color >> 8) & 0xFF), (byte)((color >> 16) & 0xFF));

    public static RgbColor ParseVbHex(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.StartsWith("&H", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        if (normalized.Length != 6 || !int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
            throw new FormatException("El color debe tener formato &HRRGGBB.");
        return new RgbColor((byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }
}
