using System.Drawing.Drawing2D;
using AoProject.WorldEditor.Application;

namespace AoProject.WorldEditor.Presentation.Features.World;

/// <summary>
/// Radar strip: a crop of the world minimap centered on the player (VB6 client
/// RenderMinimapCentered) or the whole map minimap in single-map mode. Clicking
/// reports the offset from the center so the window can recenter the map.
/// </summary>
internal sealed class WorldRadarControl : Control
{
    private const int Pad = 8;

    private Bitmap? _image;
    private int _zoom = 1;
    private int _originX = Pad;
    private int _originY = Pad;
    private int _markerX = -1;
    private int _markerY = -1;

    public WorldRadarControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(20, 24, 30);
        DoubleBuffered = true;
        MouseClick += (_, e) =>
        {
            if (_image is null) return;
            var tileX = (e.X - _originX) / Math.Max(1, _zoom);
            var tileY = (e.Y - _originY) / Math.Max(1, _zoom);
            var (markerX, markerY) = MarkerPixel();
            TileClicked?.Invoke(tileX - markerX, tileY - markerY);
        };
    }

    /// <summary>Tile offset from the radar center (0,0 is the crosshair).</summary>
    public event Action<int, int>? TileClicked;

    /// <summary>
    /// Sets the image and, optionally, the pixel that marks the player. Without a
    /// marker the crosshair stays in the middle of the image.
    /// </summary>
    public void SetImage(Bitmap? image, int? markerX = null, int? markerY = null)
    {
        if (!ReferenceEquals(image, _image)) _image?.Dispose();
        _image = image;
        _markerX = markerX ?? -1;
        _markerY = markerY ?? -1;
        Invalidate();
    }

    private (int X, int Y) MarkerPixel()
    {
        if (_image is null) return (0, 0);
        return (_markerX >= 0 ? _markerX : _image.Width / 2,
            _markerY >= 0 ? _markerY : _image.Height / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image is null)
        {
            using var empty = new SolidBrush(Color.FromArgb(70, 70, 70));
            e.Graphics.DrawString("Radar sin datos", Font, empty, Pad, Pad);
            return;
        }
        // A 200 px world crop is shown 1:1; the 100 px map minimap is shown at 2x.
        var available = Math.Max(16, Math.Min(ClientSize.Width, ClientSize.Height) - Pad * 2);
        _zoom = Math.Max(1, available / _image.Width);
        var size = _image.Width * _zoom;
        // Center the image in the panel: the sidebar is wider than the radar, so
        // pinning it to the padding left the crop visibly off-center.
        _originX = Math.Max(Pad, (ClientSize.Width - size) / 2);
        // Keep the crop immediately below the RADAR title, matching the
        // MAPA/minimap section. The radar panel may be taller than the crop,
        // but that extra space belongs below the image, never above it.
        _originY = Pad;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.DrawImage(_image, new Rectangle(_originX, _originY, size, size));
        using (var border = new Pen(Color.FromArgb(90, 90, 90)))
            e.Graphics.DrawRectangle(border, _originX - 1, _originY - 1, size + 1, size + 1);
        using (var cross = new Pen(Color.FromArgb(230, 255, 255, 0), 1))
        {
            var (markerX, markerY) = MarkerPixel();
            var cx = _originX + markerX * _zoom;
            var cy = _originY + markerY * _zoom;
            e.Graphics.DrawLine(cross, cx - 5, cy, cx + 5, cy);
            e.Graphics.DrawLine(cross, cx, cy - 5, cx, cy + 5);
        }

    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
