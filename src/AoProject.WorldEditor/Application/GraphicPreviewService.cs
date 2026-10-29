using System.Drawing;
using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

/// <summary>Creates the same first-frame GRH preview used by Form1.</summary>
internal sealed class GraphicPreviewService
{
    private readonly string _resourceRoot;

    public GraphicPreviewService(string resourceRoot) => _resourceRoot = resourceRoot;

    public Bitmap? Create(GraphicData graphic)
    {
        ArgumentNullException.ThrowIfNull(graphic);
        if (graphic.PixelWidth <= 0 || graphic.PixelHeight <= 0 || graphic.FileNumber <= 0) return null;
        var source = new GraphicSourceResolver(_resourceRoot).Resolve(graphic);
        using var bitmap = new Bitmap(source.Path);
        if (source.SourceX < 0 || source.SourceY < 0 || source.SourceX + source.Width > bitmap.Width || source.SourceY + source.Height > bitmap.Height)
            throw new InvalidDataException("El recorte del GRH está fuera de la textura.");
        var preview = new Bitmap(source.Width, source.Height);
        using var canvas = Graphics.FromImage(preview);
        canvas.DrawImage(bitmap, new Rectangle(0, 0, source.Width, source.Height),
            new Rectangle(source.SourceX, source.SourceY, source.Width, source.Height), GraphicsUnit.Pixel);
        return preview;
    }
}
