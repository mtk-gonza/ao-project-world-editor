namespace AoProject.WorldEditor.Presentation.Features.World;

/// <summary>
/// Right sidebar: "MAPA" with the literal map overview and "RADAR" with the
/// centered crop, each with its own title.
/// </summary>
internal sealed class WorldSidebar : Panel
{
    private readonly Label _radarLabel;
    private readonly Label _worldLabel;
    private readonly TableLayoutPanel _layout;
    private readonly TableLayoutPanel _radarSection;

    public WorldSidebar()
    {
        Dock = DockStyle.Right;
        Width = 232;
        BackColor = Color.FromArgb(20, 24, 30);
        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 4, 0, 0)
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 226));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));

        _worldLabel = Title("MAPA");
        MiniMap = new WorldMiniMapControl { Dock = DockStyle.Fill };
        _radarLabel = Title("RADAR");
        Radar = new WorldRadarControl { Dock = DockStyle.Fill };
        _radarSection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _radarSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _radarSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        _radarSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _radarSection.Controls.Add(_radarLabel, 0, 0);
        _radarSection.Controls.Add(Radar, 0, 1);

        _layout.Controls.Add(_worldLabel, 0, 0);
        _layout.Controls.Add(MiniMap, 0, 1);
        _layout.Controls.Add(_radarSection, 0, 2);
        Controls.Add(_layout);
        SetRadarVisible(false);
    }

    public WorldMiniMapControl MiniMap { get; }

    public WorldRadarControl Radar { get; }

    /// <summary>Shows or hides the radar and its title together.</summary>
    public void SetRadarVisible(bool visible)
    {
        _radarLabel.Visible = visible;
        Radar.Visible = visible;
        _layout.RowStyles[2] = new RowStyle(SizeType.Absolute, visible ? 230 : 0);
        _radarSection.RowStyles[0] = new RowStyle(SizeType.Absolute, visible ? 20 : 0);
        _radarSection.RowStyles[1] = new RowStyle(SizeType.Percent, visible ? 100 : 0);
        _layout.PerformLayout();
    }

    private static Label Title(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 20,
        Dock = DockStyle.Top,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        ForeColor = Color.FromArgb(190, 200, 210)
    };
}
