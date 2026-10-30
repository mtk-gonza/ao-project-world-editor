using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Presentation.Features.Editor;

internal sealed class EditorInspectorPanel : Panel
{
    public event Action<string>? ActionRequested;
    private readonly Label _title;
    private readonly TextBox _details;
    private readonly ListBox _history = new() { Dock = DockStyle.Bottom, Height = 82, IntegralHeight = false };

    public EditorInspectorPanel()
    {
        Dock = DockStyle.Right;
        Width = 300;
        BackColor = Color.FromArgb(28, 32, 39);
        _title = new Label { Text = "INSPECTOR", Dock = DockStyle.Top, Height = 28, Padding = new Padding(8), ForeColor = Color.White, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        _details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(20, 24, 30), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None, Padding = new Padding(8) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 116, Padding = new Padding(6), BackColor = Color.FromArgb(28, 32, 39) };
        AddAction(actions, "Editar objeto", "object"); AddAction(actions, "Editar NPC", "npc");
        AddAction(actions, "Editar trigger", "trigger"); AddAction(actions, "Bloqueo por lados", "blocking");
        AddAction(actions, "Editar luz", "light"); AddAction(actions, "Editar partícula", "particle");
        AddAction(actions, "Editar salida", "transfer"); AddAction(actions, "Metadata del mapa", "metadata");
        _history.BackColor = Color.FromArgb(20, 24, 30); _history.ForeColor = Color.Gainsboro;
        Controls.Add(_details); Controls.Add(_history); Controls.Add(actions); Controls.Add(_title);
        ShowEmpty();
    }

    public void AddHistory(string description)
    {
        _history.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {description}");
        while (_history.Items.Count > 30) _history.Items.RemoveAt(_history.Items.Count - 1);
    }

    public void ClearHistory() => _history.Items.Clear();

    private void AddAction(Control parent, string text, string action)
    {
        var button = new Button { Text = text, Width = 128, Height = 30 };
        button.Click += (_, _) => ActionRequested?.Invoke(action);
        parent.Controls.Add(button);
    }

    public void ShowTile(WorldSector map, int x, int y)
    {
        if (x < 1 || x > WorldSector.Width || y < 1 || y > WorldSector.Height) { ShowEmpty(); return; }
        var block = map.Blocks[x, y];
        var graphics = string.Join(", ", block.Graphics.Select((graphic, index) => $"C{index + 1}:{graphic.GraphicIndex}"));
        _details.Text = $"Local: {x},{y}\r\n" +
            $"Mapa origen: {map.Info.Name}\r\n\r\n" +
            $"Graficos: {graphics}\r\n" +
            $"Blqs: {block.Blocked} (N={((block.Blocked & BlockedMask.North) != 0 ? 1 : 0)}, E={((block.Blocked & BlockedMask.East) != 0 ? 1 : 0)}, S={((block.Blocked & BlockedMask.South) != 0 ? 1 : 0)}, O={((block.Blocked & BlockedMask.West) != 0 ? 1 : 0)})\r\n" +
            $"Trigger: {block.Trigger}\r\n" +
            $"Objeto: {block.Object.Index} x{block.Object.Amount}\r\n" +
            $"NPC: {block.NpcIndex}\r\n" +
            $"Particula: grupo {block.ParticleGroup}, indice {block.ParticleIndex}\r\n" +
            $"Luz: rango {block.Light.Range}, color {block.Light.Color}\r\n" +
            $"Salida legacy: {block.TileExit.Map} ({block.TileExit.X},{block.TileExit.Y})";
    }

    public void ShowUnifiedTile(UnifiedWorldMap map, int x, int y)
    {
        var gx = Math.Clamp(x, 0, map.Width - 1);
        var gy = Math.Clamp(y, 0, map.Height - 1);
        var offset = map.Offset(gx, gy);
        var graphics = string.Join(", ", Enumerable.Range(0, 8)
            .Select(layer => $"C{layer + 1}:{map.GraphicIndices[map.GraphicOffset(offset, layer)]}"));
        _details.Text = $"Global: {gx},{gy} | Z={map.FloorZ}\r\n\r\n" +
            $"Graficos: {graphics}\r\n" +
            $"Bloqueo: {map.Blocked[offset]}\r\n" +
            $"Trigger: {map.Trigger[offset]}\r\n" +
            $"Objeto: {map.ObjectIndex[offset]} x{map.ObjectAmount[offset]}\r\n" +
            $"NPC: {map.NpcIndex[offset]}\r\n" +
            $"Particula: grupo {map.ParticleGroup[offset]}, indice {map.ParticleIndex[offset]}\r\n" +
            $"Salida: {map.ExitIndex[offset]}";
    }

    public void ShowEmpty() => _details.Text = "Seleccione un tile para ver sus propiedades.";
}
