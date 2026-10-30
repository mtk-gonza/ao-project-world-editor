using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Presentation.Features.Editor;

internal sealed class EditorPalettePanel : Panel
{
    public event Action<int>? TerrainPresetSelected;
    public event Action<int>? LayerSelected;
    public event Action<string>? ActionSelected;
    public event Action<int>? BrushSizeChanged;
    public event Action<string>? BrushShapeChanged;
    public event Action<short>? ObjectSelected;
    public event Action<short>? NpcSelected;
    public event Action<short>? TriggerSelected;
    public event Action<int>? GraphicSelected;
    public event Action<SurfaceData>? SurfaceSelected;
    public event Action<int>? ParticleSelected;
    public event Action<short, int>? LightSelected;
    public event Action<short, short, short>? TransferSelected;
    public event Action? GlobalTransferSelected;
    public event Action<int>? TabChanged;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ListBox _objects = new() { Dock = DockStyle.Fill };
    private readonly ListBox _npcs = new() { Dock = DockStyle.Fill };
    private readonly ListBox _triggers = new() { Dock = DockStyle.Fill };
    private readonly ListBox _graphics = new() { Dock = DockStyle.Fill };
    private readonly ListBox _surfaces = new() { Dock = DockStyle.Fill };
    private readonly ListBox _particles = new() { Dock = DockStyle.Fill };
    private readonly List<int> _objectIds = [];
    private readonly List<int> _npcIds = [];
    private readonly List<int> _triggerIds = [];
    private readonly List<string> _objectLabels = [];
    private readonly List<string> _npcLabels = [];
    private readonly List<string> _triggerLabels = [];
    private readonly List<int> _graphicIds = [];
    private readonly List<string> _graphicLabels = [];
    private readonly List<SurfaceData> _surfaceData = [];
    private readonly List<int> _surfaceIds = [];
    private readonly List<string> _surfaceLabels = [];
    private readonly List<int> _particleIds = [];
    private readonly List<string> _particleLabels = [];

    public EditorPalettePanel()
    {
        Dock = DockStyle.Left;
        Width = 238;
        BackColor = Color.FromArgb(28, 32, 39);
        _tabs.TabPages.Add(TerrainPage());
        _tabs.TabPages.Add(LayersPage());
        _tabs.TabPages.Add(SimplePage("Decoración", "Árboles", "Rocas", "Carteles", "Puertas", "Ventanas"));
        _tabs.TabPages.Add(ObjectsPage());
        _tabs.TabPages.Add(NpcsPage());
        _tabs.TabPages.Add(TriggersPage());
        _tabs.TabPages.Add(LightsPage());
        _tabs.TabPages.Add(ParticlesPage());
        _tabs.TabPages.Add(TransfersPage());
        _tabs.TabPages.Add(SimplePage("Especiales", "Selector", "Cuentagotas", "Borrador", "Relleno", "Reemplazar", "Bloquear", "Desbloquear", "Bloqueo por lados", "Línea", "Rectángulo", "Auto-border", "Medir", "Copiar", "Cortar", "Pegar", "Ver bloqueos", "Ver triggers", "Ver objetos", "Ver NPCs", "Ver luces", "Ver partículas", "Ver salidas"));
        _tabs.SelectedIndexChanged += (_, _) => TabChanged?.Invoke(_tabs.SelectedIndex);
        Controls.Add(_tabs);
    }

    public int SelectedTabIndex => _tabs.SelectedIndex;

    public void SelectTab(int index)
    {
        if (_tabs.TabCount == 0) return;
        _tabs.SelectedIndex = Math.Clamp(index, 0, _tabs.TabCount - 1);
    }

    public void ConfigureResources(IReadOnlyList<GraphicData> graphics, IReadOnlyList<SurfaceData> surfaces,
        IReadOnlyList<ObjectData> objects, IReadOnlyList<NpcData> npcs, IReadOnlyList<TriggerData> triggers,
        IReadOnlyList<ParticleData> particles)
    {
        _graphicIds.Clear(); _graphicLabels.Clear(); _graphics.Items.Clear();
        for (var i = 0; i < graphics.Count; i++)
        {
            _graphicIds.Add(i);
            var graphic = graphics[i];
            _graphicLabels.Add(graphic is null
                ? $"{i}: (sin datos)"
                : $"{i}: archivo {graphic.FileNumber} ({graphic.PixelWidth}x{graphic.PixelHeight})");
        }
        _graphics.Items.AddRange(_graphicLabels.Take(1000).ToArray());
        _surfaceData.Clear(); _surfaceIds.Clear(); _surfaceLabels.Clear(); _surfaces.Items.Clear();
        for (var i = 0; i < surfaces.Count; i++)
        {
            var surface = surfaces[i] ?? new SurfaceData { Name = "(sin datos)" };
            _surfaceData.Add(surface); _surfaceIds.Add(i);
            _surfaceLabels.Add($"{i}: {surface.Name} GRH {surface.GraphicIndex}");
        }
        _surfaces.Items.AddRange(_surfaceLabels.ToArray());
        _objectIds.Clear(); _objectLabels.Clear(); _objects.Items.Clear();
        for (var i = 0; i < objects.Count; i++)
        {
            _objectIds.Add(i); _objectLabels.Add($"{i}: {objects[i]?.Name ?? "(sin datos)"}");
        }
        _objects.Items.AddRange(_objectLabels.ToArray());
        _npcIds.Clear(); _npcLabels.Clear(); _npcs.Items.Clear();
        for (var i = 0; i < npcs.Count; i++)
        {
            _npcIds.Add(i); _npcLabels.Add($"{i}: {npcs[i]?.Name ?? "(sin datos)"}");
        }
        _npcs.Items.AddRange(_npcLabels.ToArray());
        _triggerIds.Clear(); _triggerLabels.Clear(); _triggers.Items.Clear();
        foreach (var trigger in triggers)
        {
            if (trigger is null) continue;
            _triggerIds.Add(trigger.Index);
            _triggerLabels.Add($"{trigger.Index}: {trigger.Name}");
        }
        _triggers.Items.AddRange(_triggerLabels.ToArray());
        _particleIds.Clear(); _particleLabels.Clear(); _particles.Items.Clear();
        foreach (var particle in particles)
        {
            if (particle is null) continue;
            _particleIds.Add(particle.Id);
            _particleLabels.Add($"{particle.Id}: {particle.Name}");
        }
        _particles.Items.AddRange(_particleLabels.ToArray());
    }

    private TabPage TerrainPage()
    {
        var page = new TabPage("Terreno");
        var panel = ButtonPanel();
        panel.Controls.Add(new Label { Text = "Tamaño del brush", Width = 190, ForeColor = Color.Gainsboro });
        var size = new NumericUpDown { Minimum = 1, Maximum = 20, Value = 1, Width = 190 };
        size.ValueChanged += (_, _) => BrushSizeChanged?.Invoke((int)size.Value);
        panel.Controls.Add(size);
        panel.Controls.Add(new Label { Text = "Forma", Width = 190, ForeColor = Color.Gainsboro });
        var shape = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
        shape.Items.AddRange(["Individual", "Cuadrado", "Circular"]);
        shape.SelectedIndex = 1;
        shape.SelectedIndexChanged += (_, _) => BrushShapeChanged?.Invoke(shape.Text);
        panel.Controls.Add(shape);
        AddPreset(panel, "Pasto", 0); AddPreset(panel, "Agua", 137); AddPreset(panel, "Arena", 245);
        AddPreset(panel, "Hielo", 621); AddPreset(panel, "Piedra", 1);
        panel.Controls.Add(new Label { Text = "Buscar GRH", Width = 190, ForeColor = Color.Gainsboro });
        var graphicSearch = new TextBox { PlaceholderText = "Índice o archivo...", Width = 190 };
        graphicSearch.TextChanged += (_, _) => Filter(_graphics, _graphicIds, _graphicLabels, graphicSearch.Text, 2000);
        _graphics.Height = 150;
        _graphics.SelectedIndexChanged += (_, _) =>
        {
            if (_graphics.SelectedIndex >= 0) GraphicSelected?.Invoke(_graphicIds[_graphics.SelectedIndex]);
        };
        panel.Controls.Add(graphicSearch); panel.Controls.Add(_graphics);
        panel.Controls.Add(new Label { Text = "Buscar superficie", Width = 190, ForeColor = Color.Gainsboro });
        var surfaceSearch = new TextBox { PlaceholderText = "Nombre o GRH...", Width = 190 };
        surfaceSearch.TextChanged += (_, _) => Filter(_surfaces, _surfaceIds, _surfaceLabels, surfaceSearch.Text);
        _surfaces.Height = 150;
        _surfaces.SelectedIndexChanged += (_, _) =>
        {
            if (_surfaces.SelectedIndex >= 0) SurfaceSelected?.Invoke(_surfaceData[_surfaceIds[_surfaces.SelectedIndex]]);
        };
        panel.Controls.Add(surfaceSearch); panel.Controls.Add(_surfaces);
        page.Controls.Add(panel); return page;
    }

    private TabPage LayersPage()
    {
        var page = new TabPage("Capas");
        var panel = ButtonPanel();
        for (var layer = 0; layer < 4; layer++)
        {
            var selected = layer;
            var button = new Button { Text = $"Capa {layer + 1}", Width = 190, Height = 32 };
            button.Click += (_, _) => LayerSelected?.Invoke(selected);
            panel.Controls.Add(button);
        }
        page.Controls.Add(panel); return page;
    }

    private TabPage ObjectsPage()
    {
        var page = new TabPage("Objetos");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        var search = new TextBox { PlaceholderText = "Buscar objeto...", Dock = DockStyle.Top };
        var place = new Button { Text = "Colocar objeto seleccionado", Dock = DockStyle.Top, Height = 30 };
        search.TextChanged += (_, _) => Filter(_objects, _objectIds, _objectLabels, search.Text);
        _objects.SelectedIndexChanged += (_, _) => { if (_objects.SelectedIndex >= 0) ObjectSelected?.Invoke((short)_objectIds[_objects.SelectedIndex]); };
        place.Click += (_, _) => ActionSelected?.Invoke("Colocar objeto");
        var actions = EntityActions("Editar objeto", "Borrar objeto");
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(search, 0, 0); layout.Controls.Add(place, 0, 1); layout.Controls.Add(actions, 0, 2); layout.Controls.Add(_objects, 0, 3);
        page.Controls.Add(layout); return page;
    }

    private TabPage NpcsPage()
    {
        var page = new TabPage("NPCs");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        var search = new TextBox { PlaceholderText = "Buscar NPC...", Dock = DockStyle.Top };
        var place = new Button { Text = "Colocar NPC seleccionado", Dock = DockStyle.Top, Height = 30 };
        search.TextChanged += (_, _) => Filter(_npcs, _npcIds, _npcLabels, search.Text);
        _npcs.SelectedIndexChanged += (_, _) => { if (_npcs.SelectedIndex >= 0) NpcSelected?.Invoke((short)_npcIds[_npcs.SelectedIndex]); };
        place.Click += (_, _) => ActionSelected?.Invoke("Colocar NPC");
        var actions = EntityActions("Editar NPC", "Borrar NPC");
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(search, 0, 0); layout.Controls.Add(place, 0, 1); layout.Controls.Add(actions, 0, 2); layout.Controls.Add(_npcs, 0, 3);
        page.Controls.Add(layout); return page;
    }

    private TabPage TriggersPage()
    {
        var page = new TabPage("Triggers");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        var search = new TextBox { PlaceholderText = "Buscar trigger...", Dock = DockStyle.Top };
        var place = new Button { Text = "Colocar trigger seleccionado", Dock = DockStyle.Top, Height = 30 };
        search.TextChanged += (_, _) => Filter(_triggers, _triggerIds, _triggerLabels, search.Text);
        _triggers.SelectedIndexChanged += (_, _) =>
        {
            if (_triggers.SelectedIndex >= 0) TriggerSelected?.Invoke((short)_triggerIds[_triggers.SelectedIndex]);
        };
        place.Click += (_, _) => ActionSelected?.Invoke("Colocar trigger");
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var actions = EntityActions("Editar trigger", "Borrar trigger");
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(search, 0, 0); layout.Controls.Add(place, 0, 1); layout.Controls.Add(actions, 0, 2); layout.Controls.Add(_triggers, 0, 3);
        page.Controls.Add(layout); return page;
    }

    private FlowLayoutPanel EntityActions(string editAction, string clearAction)
    {
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 68, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        var edit = new Button { Text = "Editar tile", Width = 120, Height = 30 };
        edit.Click += (_, _) => ActionSelected?.Invoke(editAction);
        var clear = new Button { Text = "Borrar tile", Width = 120, Height = 30 };
        clear.Click += (_, _) => ActionSelected?.Invoke(clearAction);
        actions.Controls.Add(edit); actions.Controls.Add(clear);
        return actions;
    }

    private TabPage ParticlesPage()
    {
        var page = new TabPage("Partículas");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        var search = new TextBox { PlaceholderText = "Buscar partícula...", Dock = DockStyle.Top };
        var place = new Button { Text = "Colocar partícula seleccionada", Dock = DockStyle.Top, Height = 30 };
        search.TextChanged += (_, _) => Filter(_particles, _particleIds, _particleLabels, search.Text);
        _particles.SelectedIndexChanged += (_, _) =>
        {
            if (_particles.SelectedIndex >= 0) ParticleSelected?.Invoke(_particleIds[_particles.SelectedIndex]);
        };
        place.Click += (_, _) => ActionSelected?.Invoke("Colocar partícula");
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 68, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        var edit = new Button { Text = "Editar tile", Width = 120, Height = 30 };
        edit.Click += (_, _) => ActionSelected?.Invoke("Editar partícula");
        var clear = new Button { Text = "Borrar tile", Width = 120, Height = 30 };
        clear.Click += (_, _) => ActionSelected?.Invoke("Borrar partícula");
        actions.Controls.Add(edit); actions.Controls.Add(clear);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowCount = 4;
        layout.Controls.Add(search, 0, 0); layout.Controls.Add(place, 0, 1); layout.Controls.Add(actions, 0, 2); layout.Controls.Add(_particles, 0, 3);
        page.Controls.Add(layout); return page;
    }

    private TabPage LightsPage()
    {
        var page = new TabPage("Luces");
        var panel = ButtonPanel();
        panel.Controls.Add(new Label { Text = "Rango", Width = 190, ForeColor = Color.Gainsboro });
        var range = new NumericUpDown { Minimum = 1, Maximum = 255, Value = 8, Width = 190 };
        panel.Controls.Add(range);
        panel.Controls.Add(new Label { Text = "Color RGB", Width = 190, ForeColor = Color.Gainsboro });
        var color = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = 0xFFFFFF, Width = 190 };
        panel.Controls.Add(color);
        var place = new Button { Text = "Colocar luz", Width = 190, Height = 32 };
        place.Click += (_, _) => LightSelected?.Invoke((short)range.Value, (int)color.Value);
        panel.Controls.Add(place);
        var edit = new Button { Text = "Editar luz del tile", Width = 190, Height = 32 };
        edit.Click += (_, _) => ActionSelected?.Invoke("Editar luz");
        panel.Controls.Add(edit);
        var clear = new Button { Text = "Borrar luz", Width = 190, Height = 32 };
        clear.Click += (_, _) => ActionSelected?.Invoke("Borrar luz");
        panel.Controls.Add(clear);
        page.Controls.Add(panel); return page;
    }

    private TabPage TransfersPage()
    {
        var page = new TabPage("Salidas");
        var panel = ButtonPanel();
        panel.Controls.Add(new Label { Text = "Mapa destino (legacy)", Width = 190, ForeColor = Color.Gainsboro });
        var map = new NumericUpDown { Minimum = 0, Maximum = short.MaxValue, Width = 190 };
        panel.Controls.Add(map);
        panel.Controls.Add(new Label { Text = "X destino", Width = 190, ForeColor = Color.Gainsboro });
        var x = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 190 };
        panel.Controls.Add(x);
        panel.Controls.Add(new Label { Text = "Y destino", Width = 190, ForeColor = Color.Gainsboro });
        var y = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 190 };
        panel.Controls.Add(y);
        var place = new Button { Text = "Crear salida", Width = 190, Height = 32 };
        place.Click += (_, _) => TransferSelected?.Invoke((short)map.Value, (short)x.Value, (short)y.Value);
        panel.Controls.Add(place);
        var global = new Button { Text = "Usar destino global seleccionado", Width = 190, Height = 32 };
        global.Click += (_, _) => GlobalTransferSelected?.Invoke();
        panel.Controls.Add(global);
        var edit = new Button { Text = "Editar salida del tile", Width = 190, Height = 32 };
        edit.Click += (_, _) => ActionSelected?.Invoke("Editar salida");
        panel.Controls.Add(edit);
        var validate = new Button { Text = "Validar salidas", Width = 190, Height = 32 };
        validate.Click += (_, _) => ActionSelected?.Invoke("Validar salidas");
        panel.Controls.Add(validate);
        page.Controls.Add(panel); return page;
    }

    private static void Filter(ListBox list, List<int> ids, IReadOnlyList<string> labels, string query, int maxResults = int.MaxValue)
    {
        list.Items.Clear(); ids.Clear();
        for (var i = 0; i < labels.Count; i++)
            if (string.IsNullOrWhiteSpace(query) || labels[i].Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                list.Items.Add(labels[i]);
                ids.Add(int.Parse(labels[i].Split(':', 2)[0]));
                if (list.Items.Count >= maxResults) break;
            }
    }

    private void AddPreset(FlowLayoutPanel panel, string text, int graphic)
    {
        var button = new Button { Text = text, Width = 190, Height = 32 };
        button.Click += (_, _) => TerrainPresetSelected?.Invoke(graphic);
        panel.Controls.Add(button);
    }

    private TabPage SimplePage(string title, params string[] actions)
    {
        var page = new TabPage(title); var panel = ButtonPanel();
        foreach (var action in actions)
        {
            var button = new Button { Text = action, Width = 190, Height = 32 };
            button.Click += (_, _) => ActionSelected?.Invoke(action);
            panel.Controls.Add(button);
        }
        page.Controls.Add(panel); return page;
    }

    private static FlowLayoutPanel ButtonPanel() => new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = true, Padding = new Padding(8)
    };
}
