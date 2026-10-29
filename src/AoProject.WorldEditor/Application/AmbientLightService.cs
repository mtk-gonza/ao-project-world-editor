namespace AoProject.WorldEditor.Application;

internal readonly record struct AmbientColor(byte Red, byte Green, byte Blue)
{
    public int ToArgb(uint alpha = 255) => (int)((alpha << 24) | ((uint)Red << 16) | ((uint)Green << 8) | Blue);
}

/// <summary>Port of clsDX8Engine.setup_ambient and change_day_effect.</summary>
internal sealed class AmbientLightService
{
    private static readonly AmbientColor[] DayLight =
    [
        new(87, 61, 43),     // 0  noche
        new(143, 137, 135),
        new(143, 137, 135),
        new(143, 137, 135),
        new(143, 137, 135),
        new(143, 137, 135),
        new(143, 137, 135),
        new(212, 205, 207),
        new(212, 205, 207),
        new(212, 205, 207),
        new(212, 205, 207),
        new(212, 205, 207),
        new(255, 255, 255),  // 12 dia
        new(255, 255, 255),
        new(255, 250, 255),
        new(255, 240, 255),
        new(255, 230, 255),
        new(230, 230, 255),
        new(230, 230, 255),
        new(156, 142, 83),
        new(87, 61, 43),
        new(87, 61, 43),
        new(87, 61, 43),
        new(87, 61, 43),
        new(87, 61, 43)
    ];

    public AmbientColor Ambient(int hour)
    {
        if (hour < 0) hour = 0;
        if (hour >= DayLight.Length) hour = DayLight.Length - 1;
        return DayLight[hour];
    }

    public IReadOnlyList<AmbientColor> Table => DayLight;

    /// <summary>
    /// Faithful port, including the reference comparison against the running
    /// red channel (`og &lt; r`) for the green and blue branches.
    /// </summary>
    public AmbientColor ChangeDayEffect(int oldRed, int oldGreen, int oldBlue, int targetRed, int targetGreen, int targetBlue)
    {
        var red = oldRed;
        var green = oldGreen;
        var blue = oldBlue;

        if (oldRed > targetRed) red--;
        else if (oldRed < targetRed) red++;

        if (oldGreen > targetGreen) green--;
        else if (oldGreen < red) green++;

        if (oldBlue > targetBlue) blue--;
        else if (oldGreen < red) blue++;

        if (red > 255) red = 255;
        if (green > 255) green = 255;
        if (blue > 255) blue = 255;

        if (red < 0 || green < 0 || blue < 0) { red = 1; green = 1; blue = 1; }

        return new AmbientColor((byte)red, (byte)green, (byte)blue);
    }
}
