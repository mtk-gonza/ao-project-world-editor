namespace AoProject.WorldEditor.Presentation.Features.World;

/// <summary>
/// Right sidebar: "MUNDO" with the world/map overview and "RADAR" with the
/// centered crop, each with its own title.
/// </summary>
internal sealed class WorldSidebar : Panel
{
    private readonly Label _radarLabel;
    private readonly Label _worldLabel;
    private readonly TableLayoutPanel _layout;

    public WorldSidebar()
    {
        Dock = DockStyle.Right;
        Width = 232;
        BackColor = Color.FromArgb(20, 24, 30);
        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0, 4, 0, 0)
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 226));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));

        _worldLabel = Title("MUNDO");
        MiniMap = new WorldMiniMapControl { Dock = DockStyle.Fill };
        _radarLabel = Title("RADAR");
        Radar = new WorldRadarControl { Dock = DockStyle.Fill };

        _layout.Controls.Add(_worldLabel, 0, 0);
        _layout.Controls.Add(MiniMap, 0, 1);
        _layout.Controls.Add(_radarLabel, 0, 2);
        _layout.Controls.Add(Radar, 0, 3);
        Controls.Add(_layout);
    }

    public WorldMiniMapControl MiniMap { get; }

    public WorldRadarControl Radar { get; }

    /// <summary>Shows or hides the radar and its title together.</summary>
    public void SetRadarVisible(bool visible)
    {
        _radarLabel.Visible = visible;
        Radar.Visible = visible;
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
