using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Infrastructure.Runtime;
using AoProject.WorldEditor.Presentation.Features.World;
using AoProject.WorldEditor.Presentation.Features.Editor;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Presentation.Forms;

/// <summary>
/// Application shell for the VB6 WorldEditor migration.
/// Functional behavior is added only when it has a corresponding reference
/// procedure and an executable parity test.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly WorldRenderControl _renderControl;
    private readonly WorldMiniMapControl _miniMap;
    private readonly WorldEditorSession _session;
    private readonly ToolStripStatusLabel _status;
    private readonly WorldEditorSettings _settings;
    private readonly string _settingsPath;
    private readonly AudioPlaybackService _audioPlayback = new();
    private readonly MapClipboardService _clipboard = new();
    private readonly System.Windows.Forms.Timer _autoSaveTimer;
    private int _autoSaveMinutes;
    private bool _mosaicEnabled;
    private int _mosaicWidth = 1;
    private int _mosaicHeight = 1;
    private bool _terrainPresetSelected;
    private int _brushSize = 1;
    private string _brushShape = "Cuadrado";
    private string _activeTool = "Selector";
    private short _selectedObject;
    private short _selectedNpc;
    private short _selectedTrigger;
    private int _selectedParticle;
    private bool _terrainBlocked;
    private bool _autoCompleteSurfaces;
    private bool _insertBlocking;
    private bool _removeBlocking;
    private bool _globalSaveInProgress;
    private readonly System.Windows.Forms.Timer _radarTimer;
    private readonly WorldSidebar _sidebar = new();
    private readonly EditorPalettePanel _palette = new();
    private readonly EditorInspectorPanel _inspector = new();
    private readonly ToolStripLabel _coordinates = new("Global: — | Local: — | Capa: —");
    private readonly ToolStripComboBox _layerSelector = new() { Name = "activeLayer", Width = 82, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ToolStripComboBox _toolSelector = new() { Name = "activeTool", Width = 92, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _toolbarBrushSize = new() { Minimum = 1, Maximum = 20, Value = 1, Width = 48 };
    private readonly ComboBox _toolbarBrushShape = new() { Width = 84, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly WorldRadarControl _radarView;
    private bool _radarEnabled;
    private bool _radarDirty = true;
    private int _radarX = int.MinValue;
    private int _radarY = int.MinValue;
    private UnifiedWorldMap? _compiledUnifiedWorld;
    private UnifiedWorldBundle? _compiledUnifiedBundle;
    private IReadOnlyDictionary<int, string>? _previewFloorImages;
    private int? _previewFloorZ;
    private bool _compileFloorsAfterWorldLoad;

    public MainForm()
    {
        _session = new WorldEditorSession(ResourceRootResolver.Resolve());
        _settingsPath = EditorPaths.SettingsFile;
        _settings = WorldEditorSettings.Load(_settingsPath);
        _status = new ToolStripStatusLabel($"Recursos: {_session.ResourceRoot}");
        _session.ConfigureUndo(_settings.UseUndo);
        Text = "AO WorldEditor";
        StartPosition = FormStartPosition.CenterScreen;
        // Minimum 1280x768 (the client's reference resolution). On a screen whose
        // work area is smaller, clamp so the window stays usable.
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 768);
        MinimumSize = new Size(Math.Min(1280, workArea.Width), Math.Min(768, workArea.Height));
        Size = MinimumSize;
        _renderControl = new WorldRenderControl { Dock = DockStyle.Fill };
        _radarView = _sidebar.Radar;
        _miniMap = _sidebar.MiniMap;
        _radarView.TileClicked += (dx, dy) => CenterFromRadar(dx, dy);
        _renderControl.ViewChanged += () =>
        {
            _miniMap.Invalidate();
            if (_renderControl.SelectedTile is { } selected) UpdateCoordinateBar(selected.X, selected.Y);
        };
        // Re-crop when a minimap finishes building, so the radar fills in while
        // the world image is composed instead of waiting for the camera to move.
        _miniMap.WorldImageChanged += () => _radarDirty = true;
        _renderControl.MapBound += (map, resources) =>
        {
            _inspector.ClearHistory();
            _miniMap.SetLocalMap(map, resources, () => _renderControl.SelectedTile, _renderControl.CenterOnTile);
            _radarDirty = true;
        };
        _radarTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _radarTimer.Tick += (_, _) => UpdateRadar();
        if (Environment.GetEnvironmentVariable("AO_RADAR") == "1")
        {
            _radarEnabled = true;
            _sidebar.SetRadarVisible(true);
            _radarTimer.Start();
        }
        _renderControl.GlobalMapActivated += mapId =>
        {
            if (_session.ActivateWorldMap(mapId))
                _status.Text = $"Editando mapa {mapId} desde la vista global";
        };
        _palette.LayerSelected += layer =>
        {
            _renderControl.SelectedLayer = layer;
            if (_layerSelector.SelectedIndex != layer) _layerSelector.SelectedIndex = layer;
            _status.Text = $"Capa activa: {layer + 1}";
        };
        _palette.TerrainPresetSelected += graphic =>
        {
            _renderControl.SelectedGraphicIndex = graphic;
            _terrainPresetSelected = true;
            _status.Text = $"Terreno seleccionado: grafico {graphic}";
        };
        _palette.ActionSelected += HandlePaletteAction;
        _palette.ObjectSelected += index => { _selectedObject = index; _status.Text = $"Objeto seleccionado: {index}"; };
        _palette.NpcSelected += index => { _selectedNpc = index; _status.Text = $"NPC seleccionado: {index}"; };
        _palette.TriggerSelected += index => { _selectedTrigger = index; _status.Text = $"Trigger seleccionado: {index}"; };
        _palette.GraphicSelected += graphic =>
        {
            _renderControl.SelectedGraphicIndex = graphic;
            _terrainPresetSelected = false;
            _status.Text = $"GRH seleccionado: {graphic}";
        };
        _palette.SurfaceSelected += surface =>
        {
            _renderControl.SelectedGraphicIndex = surface.GraphicIndex;
            _renderControl.SelectedLayer = Math.Clamp(surface.Layer - 1, 0, 3);
            _terrainPresetSelected = true;
            _terrainBlocked = surface.Blocked;
            _mosaicEnabled = surface.Width > 1 || surface.Height > 1;
            _mosaicWidth = Math.Max(1, (int)surface.Width);
            _mosaicHeight = Math.Max(1, (int)surface.Height);
            _status.Text = $"Superficie seleccionada: {surface.Name} (GRH {surface.GraphicIndex})";
        };
        _palette.ParticleSelected += index => { _selectedParticle = index; _status.Text = $"Partícula seleccionada: {index}"; };
        _palette.LightSelected += (range, color) => PlacePaletteLight(range, color);
        _palette.TransferSelected += (map, x, y) => PlacePaletteTransfer(map, x, y);
        _palette.GlobalTransferSelected += PlaceGlobalPaletteTransfer;
        _palette.BrushSizeChanged += size =>
        {
            _brushSize = size;
            if ((int)_toolbarBrushSize.Value != size) _toolbarBrushSize.Value = size;
            _status.Text = $"Tamaño de brush: {size}";
        };
        _palette.BrushShapeChanged += shape =>
        {
            _brushShape = shape;
            if (_toolbarBrushShape.SelectedItem as string != shape) _toolbarBrushShape.SelectedItem = shape;
            _status.Text = $"Forma de brush: {shape}";
        };
        _palette.TabChanged += index => _settings.PaletteTab = index;
        _inspector.ActionRequested += HandleInspectorAction;
        _settings.ApplyTo(_renderControl.RenderOptions);
        _renderControl.SetZoom(_settings.ZoomPercent / 100f);
        _palette.SelectTab(_settings.PaletteTab);
        _autoSaveTimer = new System.Windows.Forms.Timer();
        _autoSaveTimer.Tick += (_, _) => AutoSaveTick();

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("Archivo");
        file.DropDownItems.Add(new ToolStripMenuItem("Nuevo mapa", null, NewMap));
         var open = new ToolStripMenuItem("Abrir mapa...", null, OpenMap);
         var openWorld = new ToolStripMenuItem("Abrir mundo completo...", null, OpenWorld);
         var openOriginalMaps = new ToolStripMenuItem("Cargar mapas originales (/Mapas)...", null, OpenOriginalMaps);
         var openUnified = new ToolStripMenuItem("Abrir mapa único (*.gmap)...", null, OpenUnifiedWorld);
         var openBundle = new ToolStripMenuItem("Abrir bundle de pisos...", null, OpenUnifiedWorldBundle);
        var switchWorld = new ToolStripMenuItem("Cambiar mundo/nivel...", null, SwitchWorldLevel);
        var save = new ToolStripMenuItem("Guardar mapa...", null, SaveMap);
        var saveAs = new ToolStripMenuItem("Guardar mapa como...", null, SaveMapAs);
        var reopen = new ToolStripMenuItem("Reabrir mapa", null, ReopenMap);
        var edit = new ToolStripMenuItem("Editar");
        var undo = new ToolStripMenuItem("Deshacer", null, UndoMenu) { ShortcutKeys = Keys.Control | Keys.Z };
        edit.DropDownItems.Add(undo);
        var useUndo = new ToolStripMenuItem("Utilizar deshacer") { CheckOnClick = true, Checked = _settings.UseUndo };
        useUndo.Click += (_, _) => { _settings.UseUndo = useUndo.Checked; _session.ConfigureUndo(useUndo.Checked); };
        edit.DropDownItems.Add(useUndo);
        var walkMode = new ToolStripMenuItem("Modo caminata") { CheckOnClick = true };
        walkMode.Click += (_, _) => { _renderControl.NavigationMode = walkMode.Checked; _status.Text = walkMode.Checked ? "Modo caminata: arrastre con el botón izquierdo" : "Modo edición"; };
        edit.DropDownItems.Add(walkMode);
        edit.DropDownItems.Add(new ToolStripMenuItem("Pegar casa desde mapa 169...", null, PasteHouse));
        edit.DropDownItems.Add(new ToolStripMenuItem("Copiar tile seleccionado", null, CopySelectedTile));
        edit.DropDownItems.Add(new ToolStripMenuItem("Cortar tile seleccionado", null, CutSelectedTile));
        edit.DropDownItems.Add(new ToolStripMenuItem("Pegar tile seleccionado", null, PasteSelectedTile));
        var layers = new ToolStripMenuItem("Capa");
        for (var layer = 0; layer < 4; layer++)
        {
            var selectedLayer = layer;
            layers.DropDownItems.Add(new ToolStripMenuItem($"Capa {layer + 1}", null,
                (_, _) => _renderControl.SelectedLayer = selectedLayer));
        }
         file.DropDownItems.Add(open);
         file.DropDownItems.Add(openOriginalMaps);
         file.DropDownItems.Add(openWorld);
         file.DropDownItems.Add(new ToolStripSeparator());
         file.DropDownItems.Add(openUnified);
         file.DropDownItems.Add(openBundle);
         file.DropDownItems.Add(new ToolStripMenuItem("Piso anterior", null, (_, _) => ChangeUnifiedFloor(-1)));
         file.DropDownItems.Add(new ToolStripMenuItem("Piso siguiente", null, (_, _) => ChangeUnifiedFloor(1)));
         file.DropDownItems.Add(new ToolStripSeparator());
         file.DropDownItems.Add(switchWorld);
         file.DropDownItems.Add(new ToolStripSeparator());
         file.DropDownItems.Add(save);
         file.DropDownItems.Add(saveAs);
         file.DropDownItems.Add(new ToolStripSeparator());
         file.DropDownItems.Add(new ToolStripMenuItem("Guardar como world.floors...", null, SaveUnifiedWorldBundleAs));
         file.DropDownItems.Add(new ToolStripMenuItem("Compilar mapa único legacy (*.gmap)...", null, SaveUnifiedWorldAs));
        file.DropDownItems.Add(reopen);
        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(layers);
        var view = new ToolStripMenuItem("Ver");
        view.DropDownItems.Add(CreateViewToggle("Capa 1", () => _renderControl.RenderOptions.ShowLayer1, value => _renderControl.RenderOptions.ShowLayer1 = value));
        view.DropDownItems.Add(CreateViewToggle("Capa 2", () => _renderControl.RenderOptions.ShowLayer2, value => _renderControl.RenderOptions.ShowLayer2 = value));
        view.DropDownItems.Add(CreateViewToggle("Capa 3", () => _renderControl.RenderOptions.ShowLayer3, value => _renderControl.RenderOptions.ShowLayer3 = value));
        view.DropDownItems.Add(CreateViewToggle("Capa 4", () => _renderControl.RenderOptions.ShowLayer4, value => _renderControl.RenderOptions.ShowLayer4 = value));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(CreateViewToggle("Grilla", () => _renderControl.RenderOptions.ShowGrid, value => _renderControl.RenderOptions.ShowGrid = value));
        view.DropDownItems.Add(CreateViewToggle("Fundido de techos", () => _renderControl.RenderOptions.ShowRoofFade, value => _renderControl.RenderOptions.ShowRoofFade = value));
        view.DropDownItems.Add(CreateViewToggle("Techos transparentes (AlphaTecho)",
            () => _renderControl.RenderOptions.AlphaRoofs,
            value => _renderControl.RenderOptions.AlphaRoofs = value));
        view.DropDownItems.Add(CreateViewToggle("Minimapa: radar (100x100)",
            () => _radarEnabled,
            value =>
            {
                _radarEnabled = value;
                if (value)
                {
                    _sidebar.SetRadarVisible(true);
                    _radarDirty = true;
                    _radarTimer.Start();
                    UpdateRadar();
                }
                else
                {
                    _radarTimer.Stop();
                    _sidebar.SetRadarVisible(false);
                }
            }));
        view.DropDownItems.Add(CreateViewToggle("Bloqueos", () => _renderControl.RenderOptions.ShowBlocked, value => _renderControl.RenderOptions.ShowBlocked = value));
        view.DropDownItems.Add(CreateViewToggle("Triggers", () => _renderControl.RenderOptions.ShowTriggers, value => _renderControl.RenderOptions.ShowTriggers = value));
        view.DropDownItems.Add(CreateViewToggle("Traslados", () => _renderControl.RenderOptions.ShowTransfers, value => _renderControl.RenderOptions.ShowTransfers = value));
        view.DropDownItems.Add(CreateViewToggle("Objetos", () => _renderControl.RenderOptions.ShowObjects, value => _renderControl.RenderOptions.ShowObjects = value));
        view.DropDownItems.Add(CreateViewToggle("NPCs", () => _renderControl.RenderOptions.ShowNpcs, value => _renderControl.RenderOptions.ShowNpcs = value));
        view.DropDownItems.Add(CreateViewToggle("Partículas", () => _renderControl.RenderOptions.ShowParticles, value => _renderControl.RenderOptions.ShowParticles = value));
        view.DropDownItems.Add(CreateViewToggle("Luces", () => _renderControl.RenderOptions.ShowLights, value => _renderControl.RenderOptions.ShowLights = value));
        var night = new ToolStripMenuItem("Iluminación ambiental") { CheckOnClick = true, Checked = true };
        night.Click += (_, _) =>
        {
            if (!_renderControl.LightingAvailable)
            {
                night.Checked = false;
                _status.Text = "Iluminacion no disponible en la vista global";
                return;
            }
            _renderControl.SetLighting(night.Checked);
            _status.Text = night.Checked ? "Iluminación ambiental activada" : "Iluminación ambiental desactivada";
        };
        view.DropDownItems.Add(night);
        menu.Items.Add(view);
        var tools = new ToolStripMenuItem("Herramientas");
        tools.DropDownItems.Add(new ToolStripMenuItem("Listado de gráficos...", null, SelectGraphic));
        tools.DropDownItems.Add(new ToolStripMenuItem("Exportar mapa a PNG...", null, ExportMapPng));
        tools.DropDownItems.Add(new ToolStripMenuItem("Exportar minimapa a PNG...", null, ExportMiniMapPng));
        tools.DropDownItems.Add(new ToolStripMenuItem("Exportar todos los mapas a PNG...", null, ExportAllMapsPng));
        tools.DropDownItems.Add(new ToolStripMenuItem("Copiar imagen del mapa al portapapeles", null, CopyMapImageToClipboard));
        tools.DropDownItems.Add(new ToolStripMenuItem("Colocar bloque...", null, PlaceBlock));
        tools.DropDownItems.Add(new ToolStripMenuItem("Optimizar mapa...", null, OptimizeMap));
        tools.DropDownItems.Add(new ToolStripMenuItem("Información del mapa...", null, EditMapInfo));
        tools.DropDownItems.Add(new ToolStripMenuItem("Auto-guardar mapa...", null, ConfigureAutoSave));
        tools.DropDownItems.Add(new ToolStripMenuItem("Actualizar índices", null, RefreshIndices));
        tools.DropDownItems.Add(new ToolStripMenuItem("Informes...", null, ShowReport));
        tools.DropDownItems.Add(new ToolStripMenuItem("Reemplazar...", null, ReplaceValues));
        tools.DropDownItems.Add(new ToolStripMenuItem("GRH a BMP...", null, LookupGraphic));
        tools.DropDownItems.Add(new ToolStripMenuItem("Información del GRH...", null, LookupGraphicInfo));
        tools.DropDownItems.Add(new ToolStripMenuItem("Opciones de visualización...", null, ConfigureRendering));
        tools.DropDownItems.Add(new ToolStripMenuItem("Buscar errores del mapa", null, ValidateMap));
        tools.DropDownItems.Add(new ToolStripMenuItem("Buscar errores en todos los mapas", null, ValidateAllMaps));
        tools.DropDownItems.Add(new ToolStripMenuItem("Validar layout oficial del mundo", null, ValidateWorldLayout));
        tools.DropDownItems.Add(new ToolStripMenuItem("Precargar mundo (memoria)...", null, PreloadWorldInteractive));
        tools.DropDownItems.Add(new ToolStripMenuItem("Auditoría completa (mapas + coordenadas)...", null, RunFullAudit));
         tools.DropDownItems.Add(new ToolStripMenuItem("Auditar world.csm/.dat/.inf...", null, RunGlobalArchiveAudit));
         tools.DropDownItems.Add(new ToolStripMenuItem("Auditar bloqueos globales...", null, RunGlobalBlockingAudit));
         tools.DropDownItems.Add(new ToolStripMenuItem("Generar informe de conflictos del mapa global...", null, GenerateUnifiedWorldConflictReport));
        tools.DropDownItems.Add(new ToolStripMenuItem("Corregir bloqueos en copia...", null, CorrectGlobalBlockingCopy));
        tools.DropDownItems.Add(new ToolStripMenuItem("Generar salidas auxiliares del mundo...", null, GenerateWorldOutputs));
        tools.DropDownItems.Add(new ToolStripMenuItem("Exportar auditoría visual de mundos...", null, ExportWorldLayoutAudit));
        tools.DropDownItems.Add(new ToolStripMenuItem("Mundos especiales...", null, ShowSpecialWorlds));
        tools.DropDownItems.Add(new ToolStripMenuItem("Traslados adyacentes...", null, ConfigureAdjacentTransfers));
        tools.DropDownItems.Add(new ToolStripMenuItem("Copiar borde a mapa vecino...", null, CopyBorderToNeighbor));
        tools.DropDownItems.Add(new ToolStripMenuItem("Completar mapas vecinos...", null, StitchAdjacentMaps));
        var autoSurface = new ToolStripMenuItem("Auto-capturar superficies") { CheckOnClick = true, Checked = _settings.AutoCaptureSurfaces };
        autoSurface.Click += (_, _) => _settings.AutoCaptureSurfaces = autoSurface.Checked;
        var autoTransfer = new ToolStripMenuItem("Auto-capturar traslados") { CheckOnClick = true, Checked = _settings.AutoCaptureTransfers };
        autoTransfer.Click += (_, _) => _settings.AutoCaptureTransfers = autoTransfer.Checked;
        tools.DropDownItems.Add(autoSurface);
        tools.DropDownItems.Add(autoTransfer);
        tools.DropDownItems.Add(new ToolStripMenuItem("Clima y luz base...", null, ConfigureEnvironment));
        tools.DropDownItems.Add(new ToolStripMenuItem("Colocar ambientación...", null, PlaceAmbientation));
        tools.DropDownItems.Add(new ToolStripMenuItem("Colocar NPCs al azar...", null, PlaceRandomNpcs));
        tools.DropDownItems.Add(new ToolStripMenuItem("Colocar objetos al azar...", null, PlaceRandomObjects));
        tools.DropDownItems.Add(new ToolStripMenuItem("Superficie al azar...", null, PlaceRandomSurface));
        tools.DropDownItems.Add(new ToolStripMenuItem("Superficie en bordes...", null, PlaceSurfaceBorders));
        tools.DropDownItems.Add(new ToolStripMenuItem("Superficie en todo el mapa...", null, PlaceSurfaceWholeMap));
        tools.DropDownItems.Add(new ToolStripMenuItem("Árboles al azar...", null, PlaceRandomTrees));
        tools.DropDownItems.Add(new ToolStripMenuItem("Bloquear árboles, carteles, foros y yacimientos", null,
            (_, _) => Destructive("aplicar bloqueos de objetos configurados", () => _ = _session.BlockConfiguredObjects())));
        var terrain = new ToolStripMenuItem("Terrenos");
        terrain.DropDownItems.Add(new ToolStripMenuItem("Pasto", null, (_, _) => ApplyTerrainPreset(0)));
        terrain.DropDownItems.Add(new ToolStripMenuItem("Agua", null, (_, _) => ApplyTerrainPreset(137)));
        terrain.DropDownItems.Add(new ToolStripMenuItem("Arena", null, (_, _) => ApplyTerrainPreset(245)));
        terrain.DropDownItems.Add(new ToolStripMenuItem("Hielo", null, (_, _) => ApplyTerrainPreset(621)));
        tools.DropDownItems.Add(terrain);
        tools.DropDownItems.Add(new ToolStripMenuItem("Convertir mapas .map a .csm...", null, ConvertMaps));
        tools.DropDownItems.Add(new ToolStripMenuItem("Acerca de...", null, ShowAbout));
        tools.DropDownItems.Add(new ToolStripMenuItem("Seleccionar música MIDI...", null, SelectMusic));
        tools.DropDownItems.Add(new ToolStripMenuItem("Reproducir música MIDI...", null, PlayMidi));
        tools.DropDownItems.Add(new ToolStripMenuItem("Reproducir WAV ambiental...", null, PlayAmbientWave));
        tools.DropDownItems.Add(new ToolStripMenuItem("Detener audio", null, StopAudio));
        menu.Items.Add(tools);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(new ToolStripMenuItem("Limpiar capa seleccionada", null, (_, _) => Destructive("limpiar la capa seleccionada", () => _session.ClearLayer(_renderControl.SelectedLayer))));
        edit.DropDownItems.Add(new ToolStripMenuItem("Bloquear bordes", null, (_, _) => Destructive("bloquear los bordes", _session.LockBorders)));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar NPCs", null, (_, _) => Destructive("quitar los NPCs", () => _ = _session.RemoveNpcs())));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar objetos", null, (_, _) => Destructive("quitar los objetos", () => _ = _session.RemoveObjects())));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar triggers", null, (_, _) => Destructive("quitar los triggers", () => _ = _session.RemoveTriggers())));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar traslados", null, (_, _) => Destructive("quitar los traslados", () => _ = _session.RemoveExits())));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(new ToolStripMenuItem("Borrar mapa completo", null, (_, _) => Destructive("borrar el mapa completo", () => _ = _session.ClearMap())));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar bordes", null, (_, _) => Destructive("quitar los bordes", () => _ = _session.RemoveBorders())));
        edit.DropDownItems.Add(new ToolStripMenuItem("Bloquear todo el mapa", null, (_, _) => Destructive("bloquear todo el mapa", () => _session.SetAllBlocked(0xF))));
        edit.DropDownItems.Add(new ToolStripMenuItem("Desbloquear todo el mapa", null, (_, _) => Destructive("desbloquear todo el mapa", () => _session.SetAllBlocked(0))));
        edit.DropDownItems.Add(new ToolStripMenuItem("Insertar luz en tile seleccionado...", null, SetSelectedLight));
        edit.DropDownItems.Add(new ToolStripMenuItem("Insertar partícula en tile seleccionado...", null, SetSelectedParticle));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar todas las luces", null, (_, _) => Destructive("quitar todas las luces", () => { _ = _session.ClearLights(); _renderControl.RefreshLightingAfterEdit(); })));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar todas las partículas", null, (_, _) => Destructive("quitar todas las partículas", () => _ = _session.ClearParticles())));
        edit.DropDownItems.Add(new ToolStripMenuItem("Bloquear tile seleccionado", null, (_, _) => SetSelectedBlocked(0xF)));
        edit.DropDownItems.Add(new ToolStripMenuItem("Desbloquear tile seleccionado", null, (_, _) => SetSelectedBlocked(0)));
        edit.DropDownItems.Add(new ToolStripMenuItem("Alternar bloqueo de la selección", null, ToggleSelectionBlocked));
         edit.DropDownItems.Add(new ToolStripMenuItem("Bloqueo por lados...", null, ApplyBlockMask));
         var autoComplete = new ToolStripMenuItem("Auto-completar superficies") { CheckOnClick = true };
         autoComplete.CheckedChanged += (_, _) => _autoCompleteSurfaces = autoComplete.Checked;
         edit.DropDownItems.Add(autoComplete);
         edit.DropDownItems.Add(CreateBlockingMode("Insertar bloqueo al hacer click", () => _insertBlocking, value => _insertBlocking = value));
         edit.DropDownItems.Add(CreateBlockingMode("Quitar bloqueo al hacer click", () => _removeBlocking, value => _removeBlocking = value));
        edit.DropDownItems.Add(new ToolStripMenuItem("Corregir costas de la selección", null, FixCoastsSelection));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar objeto del tile seleccionado", null, (_, _) => ClearSelectedEntity( true, false, false, false)));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar NPC del tile seleccionado", null, (_, _) => ClearSelectedEntity(false, true, false, false)));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar trigger del tile seleccionado", null, (_, _) => ClearSelectedEntity(false, false, true, false)));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar traslado del tile seleccionado", null, (_, _) => ClearSelectedEntity(false, false, false, true)));
        edit.DropDownItems.Add(new ToolStripMenuItem("Borrar todas las capas del tile seleccionado", null, ClearSelectedGraphics));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar luz del tile seleccionado", null, ClearSelectedLight));
        edit.DropDownItems.Add(new ToolStripMenuItem("Quitar partícula del tile seleccionado", null, ClearSelectedParticle));
        edit.DropDownItems.Add(new ToolStripMenuItem("Insertar objeto en tile seleccionado...", null, SetSelectedObject));
        edit.DropDownItems.Add(new ToolStripMenuItem("Insertar NPC en tile seleccionado...", null, SetSelectedNpc));
        edit.DropDownItems.Add(new ToolStripMenuItem("Insertar trigger en tile seleccionado...", null, SetSelectedTrigger));
        edit.DropDownItems.Add(new ToolStripMenuItem("Insertar traslado en tile seleccionado...", null, SetSelectedTransfer));

        _renderControl.TileClicked += HandleTileClicked;
        _renderControl.TileDragged += HandleTileDragged;
        _renderControl.TileGestureCompleted += HandleTileGestureCompleted;
        _renderControl.TileRightClicked += (x, y) => { InspectTile(x, y); if (_session.CurrentMap is not null) _inspector.ShowTile(_session.CurrentMap, x, y); UpdateCoordinateBar(x, y); };
        _renderControl.TileDoubleClicked += NavigateTransfer;
        _renderControl.SelectionChanged += selection => _status.Text = $"Selección: {selection.StartX},{selection.StartY} - {selection.EndX},{selection.EndY}";
        _renderControl.TileEraseRequested += EraseGraphic;
        _renderControl.UndoRequested += UndoEdit;
        var toolbar = CreateEditorToolbar();
        var status = new StatusStrip();
        status.Items.Add(_status);
        MainMenuStrip = menu;
        // Docking order is resolved from the last added control: menu (top),
        // status (bottom), sidebar (right), render (fill).
        Controls.Add(_renderControl);
        Controls.Add(_sidebar);
        Controls.Add(_inspector);
        Controls.Add(_palette);
        Controls.Add(status);
        Controls.Add(toolbar);
        Controls.Add(menu);
        Shown += (_, _) =>
        {
            // Startup is intentionally empty: the user must choose a map or WORLD
            // explicitly from File > Open. Do not restore LastMap or auto-open the
            // conventional world/world.csm, even when it exists.
            _ = WarmResourcesAsync();
        };
        FormClosed += (_, _) =>
        {
            if (_renderControl.SelectedTile is { } tile) { _settings.LastX = tile.X; _settings.LastY = tile.Y; }
            _settings.ZoomPercent = (int)Math.Round(_renderControl.Zoom * 100f);
            _settings.UpdateFrom(_renderControl.RenderOptions); _settings.Save(_settingsPath); _audioPlayback.Dispose();
        };
    }

    private async Task WarmResourcesAsync()
    {
        try
        {
            _status.Text = "Cargando catalogo de recursos en segundo plano...";
            var resources = await Task.Run(() => _session.Resources);
            if (IsDisposed) return;
            _palette.ConfigureResources(resources.Graphics, resources.Surfaces,
                resources.Objects, resources.Npcs, resources.Triggers, resources.Particles);
            if (_session.CurrentMap is null && _session.CurrentWorld is null)
                _status.Text = "Listo. Abra un mapa o un WORLD desde Archivo.";
        }
        catch (Exception error)
        {
            LogStartupError(error);
            _status.Text = "No se pudo cargar el catalogo de recursos.";
        }
    }

    private ToolStrip CreateEditorToolbar()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.FromArgb(32, 37, 45) };
        var zoomOut = new ToolStripButton("−");
        var zoomReset = new ToolStripButton("100%");
        var zoomIn = new ToolStripButton("+");
        zoomOut.Click += (_, _) => _renderControl.SetZoom(_renderControl.Zoom / 1.1f);
        zoomReset.Click += (_, _) => _renderControl.SetZoom(1f);
        zoomIn.Click += (_, _) => _renderControl.SetZoom(_renderControl.Zoom * 1.1f);
        toolbar.Items.Add(new ToolStripLabel("Zoom:"));
        toolbar.Items.Add(zoomOut); toolbar.Items.Add(zoomReset); toolbar.Items.Add(zoomIn);
        toolbar.Items.Add(new ToolStripSeparator());
        _layerSelector.Items.Clear();
        _layerSelector.Items.AddRange(["Capa 1", "Capa 2", "Capa 3", "Capa 4"]);
        _layerSelector.SelectedIndex = _renderControl.SelectedLayer;
        _layerSelector.SelectedIndexChanged += (_, _) =>
        {
            _renderControl.SelectedLayer = _layerSelector.SelectedIndex;
            _status.Text = $"Capa activa: {_layerSelector.SelectedIndex + 1}";
        };
        toolbar.Items.Add(new ToolStripLabel("Capa:")); toolbar.Items.Add(_layerSelector);
        toolbar.Items.Add(new ToolStripSeparator());
        _toolSelector.Items.AddRange(["Selector", "Cuentagotas", "Borrador", "Relleno", "Reemplazar", "Bloquear", "Desbloquear", "Línea", "Rectángulo", "Medir"]);
        _toolSelector.SelectedIndex = 0;
        _toolSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_toolSelector.SelectedItem is string tool) HandlePaletteAction(tool);
        };
        toolbar.Items.Add(new ToolStripLabel("Herramienta:")); toolbar.Items.Add(_toolSelector);
        toolbar.Items.Add(new ToolStripSeparator());
        _toolbarBrushShape.Items.AddRange(["Individual", "Cuadrado", "Circular"]);
        _toolbarBrushShape.SelectedItem = "Cuadrado";
        _toolbarBrushSize.ValueChanged += (_, _) =>
        {
            _brushSize = (int)_toolbarBrushSize.Value;
            _status.Text = $"Tamaño de brush: {_brushSize}";
        };
        _toolbarBrushShape.SelectedIndexChanged += (_, _) =>
        {
            if (_toolbarBrushShape.SelectedItem is string shape)
            {
                _brushShape = shape;
                _status.Text = $"Forma de brush: {shape}";
            }
        };
        toolbar.Items.Add(new ToolStripLabel("Brush:"));
        toolbar.Items.Add(new ToolStripControlHost(_toolbarBrushSize));
        toolbar.Items.Add(new ToolStripControlHost(_toolbarBrushShape));
        toolbar.Items.Add(new ToolStripSeparator());
        var xBox = new ToolStripTextBox { Name = "coordinateX", Width = 48, ToolTipText = "Coordenada X" };
        var yBox = new ToolStripTextBox { Name = "coordinateY", Width = 48, ToolTipText = "Coordenada Y" };
        var go = new ToolStripButton("Ir");
        go.Click += (_, _) => CenterFromToolbar(xBox.Text, yBox.Text);
        xBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) CenterFromToolbar(xBox.Text, yBox.Text); };
        yBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) CenterFromToolbar(xBox.Text, yBox.Text); };
        toolbar.Items.Add(new ToolStripLabel("X:")); toolbar.Items.Add(xBox);
        toolbar.Items.Add(new ToolStripLabel("Y:")); toolbar.Items.Add(yBox); toolbar.Items.Add(go);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_coordinates);
        return toolbar;
    }

    private void CenterFromToolbar(string xText, string yText)
    {
        if (!int.TryParse(xText, out var x) || !int.TryParse(yText, out var y))
        {
            _status.Text = "Ingrese coordenadas X e Y válidas";
            return;
        }
        try
        {
            if (_session.CurrentWorld is not null)
            {
                _renderControl.CenterOnGlobalTile(x, y);
                _status.Text = $"Centrado en coordenadas globales {x},{y}";
            }
            else
            {
                _renderControl.CenterOnTile(Math.Clamp(x, 1, WorldMap.Width), Math.Clamp(y, 1, WorldMap.Height));
                _status.Text = $"Centrado en coordenadas locales {x},{y}";
            }
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void UpdateCoordinateBar(int x, int y)
    {
        var map = _session.CurrentMap;
        var local = map is null ? "—" : $"{x},{y}";
        var global = _renderControl.SelectedGlobalTile is { } selected ? $"{selected.X},{selected.Y}" : "—";
        var floor = _renderControl.ActiveFloorZ is { } z ? $"Z: {z}" : "Z: —";
        _coordinates.Text = $"Global: {global} | {floor} | Local: {local} | Capa: {_renderControl.SelectedLayer + 1} | Zoom: {_renderControl.Zoom:P0}";
    }

    private void PlaceGraphic(int x, int y)
    {
        try
        {
            if (_activeTool == "Borrador")
            {
                _session.ClearTileGraphics(x, y);
                if (_session.CurrentMap is not null) _renderControl.RefreshAfterEdit(_session.CurrentMap);
                _status.Text = $"Gráficos borrados en {x},{y}";
                _inspector.AddHistory($"Borrador en {x},{y}");
                return;
            }
            if (_activeTool == "Relleno")
            {
                var changed = _session.FillGraphic(x, y, _renderControl.SelectedLayer, _renderControl.SelectedGraphicIndex);
                if (_session.CurrentMap is not null) _renderControl.RefreshAfterEdit(_session.CurrentMap);
                _status.Text = $"Relleno aplicado: {changed} tiles";
                _inspector.AddHistory($"Relleno: {changed} tiles");
                _activeTool = "Selector";
                return;
            }
            if (_activeTool == "Reemplazar")
            {
                var from = _session.CurrentMap!.Blocks[x, y].Graphics[_renderControl.SelectedLayer].GraphicIndex;
                var changed = _session.ReplaceGraphic(_renderControl.SelectedLayer, from, _renderControl.SelectedGraphicIndex);
                _renderControl.RefreshAfterEdit(_session.CurrentMap);
                _status.Text = $"Reemplazo aplicado: {changed} tiles";
                _inspector.AddHistory($"Reemplazo GRH {from} → {_renderControl.SelectedGraphicIndex}: {changed} tiles");
                _activeTool = "Selector";
                _toolSelector.SelectedIndex = 0;
                return;
            }
            if (_activeTool is "Bloquear" or "Desbloquear")
            {
                var blocked = _activeTool == "Bloquear" ? (byte)0xF : (byte)0;
                _session.SetTileBlocked(x, y, blocked);
                if (_session.CurrentMap is not null) _renderControl.RefreshAfterEdit(_session.CurrentMap);
                _status.Text = $"Tile {x},{y}: {(_activeTool == "Bloquear" ? "bloqueado" : "desbloqueado")}";
                _inspector.AddHistory($"{_activeTool} en {x},{y}");
                return;
            }
            var radius = (_brushSize - 1) / 2;
            for (var brushY = y - radius; brushY <= y + radius; brushY++)
            for (var brushX = x - radius; brushX <= x + radius; brushX++)
            {
                if (_brushShape == "Individual" && (brushX != x || brushY != y)) continue;
                if (_brushShape == "Circular" && ((brushX - x) * (brushX - x) + (brushY - y) * (brushY - y) > radius * radius)) continue;
                if (brushX < 1 || brushX > WorldMap.Width || brushY < 1 || brushY > WorldMap.Height) continue;
                _session.ApplyInteractiveClick(brushX, brushY, _renderControl.SelectedLayer, _renderControl.SelectedGraphicIndex,
                    _terrainPresetSelected, _autoCompleteSurfaces && _mosaicEnabled,
                    _mosaicEnabled ? _mosaicWidth : 1, _mosaicEnabled ? _mosaicHeight : 1,
                    _terrainBlocked, _insertBlocking, _removeBlocking);
            }
            if (_session.CurrentMap is not null) _renderControl.RefreshAfterEdit(_session.CurrentMap);
            _status.Text = $"Tile {x},{y}: grafico {_renderControl.SelectedGraphicIndex}";
            _inspector.AddHistory($"Pintura en {x},{y} · GRH {_renderControl.SelectedGraphicIndex}");
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Error de edicion"); }
    }

    private void HandleTileDragged(int x, int y)
    {
        // Selector is read-only. A drag while selecting must never paint with
        // whichever GRH was previously chosen in the palette.
        if (_activeTool is not "Selector" and not "Relleno" and not "Reemplazar" and not "Línea" and not "Rectángulo" and not "Medir" and not "Cuentagotas") PlaceGraphic(x, y);
    }

    private void HandleTileClicked(int x, int y)
    {
        if (_activeTool == "Cuentagotas") PickTile(x, y);
        else if (_activeTool is not "Selector" and not "Línea" and not "Rectángulo" and not "Medir") PlaceGraphic(x, y);
        if (_compiledUnifiedWorld is { } unified && _renderControl.SelectedGlobalTile is { } global)
            _inspector.ShowUnifiedTile(unified, global.X, global.Y);
        else if (_session.CurrentMap is not null) _inspector.ShowTile(_session.CurrentMap, x, y);
        UpdateCoordinateBar(x, y);
    }

    private void HandleTileGestureCompleted(int startX, int startY, int endX, int endY)
    {
        if (_activeTool == "Medir")
        {
            var horizontal = Math.Abs(endX - startX);
            var vertical = Math.Abs(endY - startY);
            var diagonal = Math.Sqrt(horizontal * horizontal + vertical * vertical);
            _status.Text = $"Medición: ΔX={horizontal}, ΔY={vertical}, Manhattan={horizontal + vertical}, diagonal={diagonal:0.##}";
            _inspector.AddHistory($"Medición {startX},{startY} → {endX},{endY}");
            return;
        }
        if (_activeTool is not "Línea" and not "Rectángulo") return;
        if (_activeTool == "Línea")
        {
            var dx = Math.Abs(endX - startX); var sx = startX < endX ? 1 : -1;
            var dy = -Math.Abs(endY - startY); var sy = startY < endY ? 1 : -1;
            var error = dx + dy; var x = startX; var y = startY;
            while (true)
            {
                PlaceGraphic(x, y);
                if (x == endX && y == endY) break;
                var twice = 2 * error;
                if (twice >= dy) { error += dy; x += sx; }
                if (twice <= dx) { error += dx; y += sy; }
            }
        }
        else
        {
            var minX = Math.Min(startX, endX); var maxX = Math.Max(startX, endX);
            var minY = Math.Min(startY, endY); var maxY = Math.Max(startY, endY);
            for (var x = minX; x <= maxX; x++)
            {
                PlaceGraphic(x, minY);
                if (maxY != minY) PlaceGraphic(x, maxY);
            }
            for (var y = minY + 1; y < maxY; y++)
            {
                PlaceGraphic(minX, y);
                if (maxX != minX) PlaceGraphic(maxX, y);
            }
        }
        _status.Text = $"{_activeTool} aplicado: {startX},{startY} → {endX},{endY}";
        _inspector.AddHistory($"{_activeTool}: {startX},{startY} → {endX},{endY}");
    }

    private void PickTile(int x, int y)
    {
        if (_session.CurrentMap is null) return;
        var block = _session.CurrentMap.Blocks[x, y];
        var capture = new MapTileCaptureService().Capture(_session.CurrentMap, x, y, _settings.AutoCaptureSurfaces, _settings.AutoCaptureTransfers);
        if (capture.Layer >= 0)
        {
            _renderControl.SelectedLayer = capture.Layer;
            _renderControl.SelectedGraphicIndex = capture.GraphicIndex;
            _terrainPresetSelected = false;
        }
        if (block.Object.Index > 0) _selectedObject = block.Object.Index;
        if (block.NpcIndex > 0) _selectedNpc = block.NpcIndex;
        _activeTool = "Selector";
        _toolSelector.SelectedIndex = 0;
        _status.Text = $"Cuentagotas: GRH {_renderControl.SelectedGraphicIndex}, capa {_renderControl.SelectedLayer + 1}";
        _inspector.AddHistory($"Cuentagotas en {x},{y}");
    }

    private void HandlePaletteAction(string action)
    {
        var toolIndex = action switch { "Selector" => 0, "Cuentagotas" => 1, "Borrador" => 2, "Relleno" => 3, "Reemplazar" => 4, "Bloquear" => 5, "Desbloquear" => 6, "Línea" => 7, "Rectángulo" => 8, "Medir" => 9, _ => -1 };
        if (toolIndex >= 0 && _toolSelector.SelectedIndex != toolIndex) _toolSelector.SelectedIndex = toolIndex;
        switch (action)
        {
            case "Árboles": PlaceRandomTrees(null, EventArgs.Empty); break;
            case "Rocas":
            case "Carteles":
            case "Puertas":
            case "Ventanas": PlaceAmbientation(null, EventArgs.Empty); break;
            case "Colocar objeto": PlacePaletteObject(); break;
            case "Editar objeto": SetSelectedObject(null, EventArgs.Empty); break;
            case "Borrar objeto": ClearSelectedEntity(true, false, false, false); break;
            case "Colocar NPC": PlacePaletteNpc(); break;
            case "Editar NPC": SetSelectedNpc(null, EventArgs.Empty); break;
            case "Borrar NPC": ClearSelectedEntity(false, true, false, false); break;
            case "Colocar trigger": PlacePaletteTrigger(); break;
            case "Editar trigger": SetSelectedTrigger(null, EventArgs.Empty); break;
            case "Borrar trigger": ClearSelectedEntity(false, false, true, false); break;
            case "Colocar partícula": PlacePaletteParticle(); break;
            case "Editar partícula": SetSelectedParticle(null, EventArgs.Empty); break;
            case "Colocar luz": SetSelectedLight(null, EventArgs.Empty); break;
            case "Editar luz": SetSelectedLight(null, EventArgs.Empty); break;
            case "Borrar luz": ClearSelectedLight(null, EventArgs.Empty); break;
            case "Borrar partícula": ClearSelectedParticle(null, EventArgs.Empty); break;
            case "Crear salida": SetSelectedTransfer(null, EventArgs.Empty); break;
            case "Editar salida": SetSelectedTransfer(null, EventArgs.Empty); break;
            case "Validar salidas": ValidateMap(null, EventArgs.Empty); break;
            case "Cuentagotas": _activeTool = "Cuentagotas"; _status.Text = "Cuentagotas activo: seleccione un tile"; break;
            case "Borrador": _activeTool = "Borrador"; _status.Text = "Borrador activo: haga clic o arrastre sobre los tiles"; break;
            case "Relleno": _activeTool = "Relleno"; _status.Text = "Relleno activo: haga clic sobre un tile"; break;
            case "Reemplazar": _activeTool = "Reemplazar"; _status.Text = "Reemplazar activo: seleccione un tile origen"; break;
            case "Bloquear": _activeTool = "Bloquear"; _status.Text = "Bloqueo activo: haga clic o arrastre"; break;
            case "Desbloquear": _activeTool = "Desbloquear"; _status.Text = "Desbloqueo activo: haga clic o arrastre"; break;
            case "Bloqueo por lados": ApplyBlockMask(null, EventArgs.Empty); break;
            case "Copiar": CopySelectedTile(null, EventArgs.Empty); break;
            case "Cortar": CutSelectedTile(null, EventArgs.Empty); break;
            case "Pegar": PasteSelectedTile(null, EventArgs.Empty); break;
            case "Línea": _activeTool = "Línea"; _status.Text = "Línea activa: arrastre desde el inicio hasta el final"; break;
            case "Rectángulo": _activeTool = "Rectángulo"; _status.Text = "Rectángulo activo: arrastre entre dos esquinas"; break;
            case "Auto-border": FixCoastsSelection(null, EventArgs.Empty); break;
            case "Medir": _activeTool = "Medir"; _status.Text = "Medición activa: arrastre entre dos tiles"; break;
            case "Selector": _activeTool = "Selector"; _status.Text = "Selector activo"; break;
            case "Ver bloqueos": ToggleOverlay("Bloqueos", () => _renderControl.RenderOptions.ShowBlocked, value => _renderControl.RenderOptions.ShowBlocked = value); break;
            case "Ver triggers": ToggleOverlay("Triggers", () => _renderControl.RenderOptions.ShowTriggers, value => _renderControl.RenderOptions.ShowTriggers = value); break;
            case "Ver objetos": ToggleOverlay("Objetos", () => _renderControl.RenderOptions.ShowObjects, value => _renderControl.RenderOptions.ShowObjects = value); break;
            case "Ver NPCs": ToggleOverlay("NPCs", () => _renderControl.RenderOptions.ShowNpcs, value => _renderControl.RenderOptions.ShowNpcs = value); break;
            case "Ver luces": ToggleOverlay("Luces", () => _renderControl.RenderOptions.ShowLights, value => _renderControl.RenderOptions.ShowLights = value); break;
            case "Ver partículas": ToggleOverlay("Partículas", () => _renderControl.RenderOptions.ShowParticles, value => _renderControl.RenderOptions.ShowParticles = value); break;
            case "Ver salidas": ToggleOverlay("Traslados", () => _renderControl.RenderOptions.ShowTransfers, value => _renderControl.RenderOptions.ShowTransfers = value); break;
            default: _status.Text = $"Herramienta seleccionada: {action}"; break;
        }
    }

    private void ToggleOverlay(string name, Func<bool> get, Action<bool> set)
    {
        var value = !get();
        set(value);
        _renderControl.RefreshWorldView();
        _status.Text = $"Overlay {name}: {(value ? "activado" : "desactivado")}";
    }

    private void HandleInspectorAction(string action)
    {
        switch (action)
        {
            case "object": SetSelectedObject(null, EventArgs.Empty); break;
            case "npc": SetSelectedNpc(null, EventArgs.Empty); break;
            case "trigger": SetSelectedTrigger(null, EventArgs.Empty); break;
            case "blocking": ApplyBlockMask(null, EventArgs.Empty); break;
            case "light": SetSelectedLight(null, EventArgs.Empty); break;
            case "particle": SetSelectedParticle(null, EventArgs.Empty); break;
            case "transfer": SetSelectedTransfer(null, EventArgs.Empty); break;
            case "metadata": EditMapInfo(null, EventArgs.Empty); break;
        }
        if (_renderControl.SelectedTile is { } tile && _session.CurrentMap is not null)
            _inspector.ShowTile(_session.CurrentMap, tile.X, tile.Y);
    }

    private void PlacePaletteObject()
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive("colocar objeto", () => _session.SetObject(tile.X, tile.Y, _selectedObject, 1));
    }

    private void PlacePaletteNpc()
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive("colocar NPC", () => _session.SetNpc(tile.X, tile.Y, _selectedNpc));
    }

    private void PlacePaletteTrigger()
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive("colocar trigger", () => _session.SetTrigger(tile.X, tile.Y, _selectedTrigger));
    }

    private void PlacePaletteParticle()
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive("colocar partícula", () => _session.SetParticle(tile.X, tile.Y, _selectedParticle));
    }

    private void PlacePaletteLight(short range, int color)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive("colocar luz", () =>
        {
            _session.SetLight(tile.X, tile.Y, range, color);
            _renderControl.RefreshLightingAfterEdit();
        });
    }

    private void PlacePaletteTransfer(short map, short x, short y)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive("crear salida", () => _session.SetTransfer(tile.X, tile.Y, map, x, y));
    }

    private void PlaceGlobalPaletteTransfer()
    {
        if (!TryGetSelectedTile(out var source) || _session.CurrentWorld is null)
        {
            _status.Text = "Abra un mundo completo y seleccione un destino global";
            return;
        }
        if (_renderControl.SelectedGlobalTile is not { } target ||
            !_session.CurrentWorld.TryResolve(target.X, target.Y, out var location))
        {
            _status.Text = "El destino global seleccionado no pertenece a un mapa resoluble";
            return;
        }
        Destructive("crear salida global", () =>
            _session.SetTransfer(source.X, source.Y, location.MapId, (short)location.LocalX, (short)location.LocalY));
    }

    private ToolStripMenuItem CreateBlockingMode(string text, Func<bool> get, Action<bool> set)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true };
        item.CheckedChanged += (_, _) =>
        {
            set(item.Checked);
            if (item.Checked && item.Owner is not null)
            {
                foreach (ToolStripItem sibling in item.Owner.Items)
                    if (sibling is ToolStripMenuItem other && other != item &&
                        (other.Text == "Insertar bloqueo al hacer click" || other.Text == "Quitar bloqueo al hacer click"))
                        other.Checked = false;
            }
        };
        return item;
    }

    private void InspectTile(int x, int y)
    {
        if (_session.CurrentMap is null) return;
        try
        {
            var capture = new MapTileCaptureService().Capture(_session.CurrentMap, x, y, _settings.AutoCaptureSurfaces, _settings.AutoCaptureTransfers);
            if (capture.Layer >= 0) { _renderControl.SelectedLayer = capture.Layer; _renderControl.SelectedGraphicIndex = capture.GraphicIndex; }
            var description = new MapTileInspector().Describe(_session.CurrentMap, x, y, _session.Resources.Objects, _session.Resources.Npcs);
            if (capture.HasTransfer) description += $" | Traslado capturado: {capture.Transfer.Map},{capture.Transfer.X},{capture.Transfer.Y}";
            _status.Text = description.Replace(Environment.NewLine, " ");
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void NavigateTransfer(int x, int y)
    {
        try
        {
            var destination = _session.ResolveTransfer(x, y);
            if (destination is null) return;
            if (_session.CurrentMap?.Info.Changed == true)
            {
                var answer = MessageBox.Show(this, "El mapa tiene cambios sin guardar. ¿Desea guardarlos antes de continuar?", "Navegación por traslado", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes && _session.CurrentMapPath is not null) _session.SaveMap(_session.CurrentMapPath);
            }
            _session.LoadMap(destination.Value.Path);
            _renderControl.SetMap(_session.CurrentMap!, _session.Resources);
            _renderControl.CenterOnTile(destination.Value.X, destination.Value.Y);
            _status.Text = $"Mapa cargado por traslado: {Path.GetFileName(destination.Value.Path)} | destino {destination.Value.X},{destination.Value.Y}";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Navegación por traslado", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void EraseGraphic(int x, int y)
    {
        try
        {
            _session.SetGraphic(x, y, _renderControl.SelectedLayer, 0);
            if (_session.CurrentMap is not null) _renderControl.RefreshAfterEdit(_session.CurrentMap);
            _status.Text = $"Tile {x},{y}: grafico borrado";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Error de edicion"); }
    }

    private void UndoEdit()
    {
        if (!_session.CanUndo || _session.CurrentMap is null) return;
        _session.Undo();
        if (_session.CurrentWorld is not null) _renderControl.RefreshWorldView();
        else _renderControl.SetMap(_session.CurrentMap, _session.Resources);
        _status.Text = "Edicion deshecha";
        _inspector.AddHistory("Deshacer");
    }

    private void UndoMenu(object? sender, EventArgs e) => UndoEdit();

    private void UpdateTitle()
    {
        var name = _session.CurrentMapPath is null ? "sin título" : Path.GetFileName(_session.CurrentMapPath);
        Text = $"AO WorldEditor - {name}";
    }

    private ToolStripMenuItem CreateViewToggle(string text, Func<bool> getter, Action<bool> setter)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = getter() };
        item.Click += (_, _) =>
        {
            setter(item.Checked);
            if (_session.CurrentWorld is not null) _renderControl.RefreshWorldView();
            else if (_session.CurrentMap is not null) _renderControl.SetMap(_session.CurrentMap, _session.Resources);
        };
        return item;
    }

    private bool TryGetSelectedTile(out (int X, int Y) tile)
    {
        if (_session.CurrentMap is not null && _renderControl.SelectedTile is { } selected) { tile = selected; return true; }
        tile = default;
        MessageBox.Show(this, "Seleccione un tile con el botón derecho.", "Edición");
        return false;
    }

    private void CopySelectedTile(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        if (_session.CurrentWorld is not null && _renderControl.SelectedGlobalSelection is { } globalSelection)
        {
            _session.CopyGlobal(_clipboard, globalSelection);
            _status.Text = $"Selección global copiada: {globalSelection.Normalize().EndX - globalSelection.Normalize().StartX + 1}x{globalSelection.Normalize().EndY - globalSelection.Normalize().StartY + 1}";
            return;
        }
        var selection = _renderControl.SelectedSelection ?? new TileSelection(tile.X, tile.Y, tile.X, tile.Y);
        _clipboard.Copy(_session.CurrentMap!, selection);
        _status.Text = $"Selección copiada: {selection.Width}x{selection.Height}";
    }

    private void CutSelectedTile(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        if (_session.CurrentWorld is not null && _renderControl.SelectedGlobalSelection is { } globalSelection)
        {
            Destructive("cortar la selección global", () => _ = _session.CutGlobal(_clipboard, globalSelection));
            return;
        }
        var selection = _renderControl.SelectedSelection ?? new TileSelection(tile.X, tile.Y, tile.X, tile.Y);
        Destructive("cortar la selección", () => _session.CutClipboard(_clipboard, selection));
    }

    private void PasteSelectedTile(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile) || !_clipboard.HasClipboard) return;
        if (_session.CurrentWorld is not null && _renderControl.SelectedGlobalTile is { } globalTile)
        {
            Destructive("pegar la selección global", () => _ = _session.PasteGlobal(_clipboard, globalTile.X, globalTile.Y));
            return;
        }
        Destructive("pegar el tile seleccionado", () => _session.PasteClipboard(_clipboard, tile.X, tile.Y));
    }

    private void SetSelectedLight(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        using var dialog = new Form { Text = "Insertar luz", Width = 320, Height = 190, StartPosition = FormStartPosition.CenterParent };
        var range = new NumericUpDown { Minimum = 1, Maximum = 255, Value = 8, Dock = DockStyle.Top };
        var color = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = 0xFFFFFF, Dock = DockStyle.Top };
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(color); dialog.Controls.Add(range); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Destructive("insertar luz", () => { _session.SetLight(tile.X, tile.Y, (short)range.Value, (int)color.Value); _renderControl.RefreshLightingAfterEdit(); });
    }

    private void SetSelectedParticle(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        using var dialog = new Form { Text = "Insertar partícula", Width = 320, Height = 150, StartPosition = FormStartPosition.CenterParent };
        var particle = new NumericUpDown { Minimum = 1, Maximum = 200000, Value = 1, Dock = DockStyle.Top };
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(particle); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK) Destructive("insertar partícula", () => _session.SetParticle(tile.X, tile.Y, (int)particle.Value));
    }

    private void SetSelectedObject(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile) || _session.Resources.Objects.Count == 0) return;
        using var dialog = new Form { Text = "Insertar objeto", Width = 360, Height = 170, StartPosition = FormStartPosition.CenterParent };
        var index = new NumericUpDown { Minimum = 0, Maximum = _session.Resources.Objects.Count - 1, Dock = DockStyle.Top };
        var amount = new NumericUpDown { Minimum = 1, Maximum = short.MaxValue, Value = 1, Dock = DockStyle.Top };
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(amount); dialog.Controls.Add(index); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK) Destructive("insertar objeto", () => _session.SetObject(tile.X, tile.Y, (short)index.Value, (short)amount.Value));
    }

    private void SetSelectedNpc(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile) || _session.Resources.Npcs.Count == 0) return;
        using var dialog = new Form { Text = "Insertar NPC", Width = 360, Height = 140, StartPosition = FormStartPosition.CenterParent };
        var index = new NumericUpDown { Minimum = 0, Maximum = _session.Resources.Npcs.Count - 1, Dock = DockStyle.Top };
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(index); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK) Destructive("insertar NPC", () => _session.SetNpc(tile.X, tile.Y, (short)index.Value));
    }

    private void SetSelectedTrigger(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        using var dialog = new Form { Text = "Insertar trigger", Width = 360, Height = 140, StartPosition = FormStartPosition.CenterParent };
        var trigger = new NumericUpDown { Minimum = 0, Maximum = short.MaxValue, Dock = DockStyle.Top };
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(trigger); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK) Destructive("insertar trigger", () => _session.SetTrigger(tile.X, tile.Y, (short)trigger.Value));
    }

    private void SetSelectedTransfer(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        using var dialog = new Form { Text = "Insertar traslado", Width = 360, Height = 200, StartPosition = FormStartPosition.CenterParent };
        var map = new NumericUpDown { Minimum = 0, Maximum = 32767, Dock = DockStyle.Top };
        var x = new NumericUpDown { Minimum = 0, Maximum = 100, Dock = DockStyle.Top };
        var y = new NumericUpDown { Minimum = 0, Maximum = 100, Dock = DockStyle.Top };
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(y); dialog.Controls.Add(x); dialog.Controls.Add(map); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK) Destructive("insertar traslado", () => _session.SetTransfer(tile.X, tile.Y, (short)map.Value, (short)x.Value, (short)y.Value));
    }

    private void SetSelectedBlocked(byte value)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive(value == 0 ? "desbloquear tile" : "bloquear tile", () => _session.SetTileBlocked(tile.X, tile.Y, value));
    }

    private void PasteHouse(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        var directory = _session.CurrentMapPath is null ? _session.ResourceRoot : Path.GetDirectoryName(_session.CurrentMapPath)!;
        using var dialog = new OpenFileDialog { Filter = "Mapa de casa (*.csm;*.map)|*.csm;*.map", InitialDirectory = directory, FileName = "mapa169.csm" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var copied = _session.PasteHouse(dialog.FileName, tile.X, tile.Y);
            _renderControl.SetMap(_session.CurrentMap!, _session.Resources);
            _status.Text = $"Casa pegada: {copied} tiles desde {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Pegar casa", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ToggleSelectionBlocked(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        if (_session.CurrentWorld is not null && _renderControl.SelectedGlobalSelection is { } globalSelection)
        {
            Destructive("alternar bloqueo de la selección global", () => _ = _session.ToggleGlobalSelectionBlocked(globalSelection));
            return;
        }
        var selection = _renderControl.SelectedSelection ?? new TileSelection(tile.X, tile.Y, tile.X, tile.Y);
        Destructive("alternar bloqueo de la selección", () => _ = _session.ToggleSelectionBlocked(selection));
    }

    private void FixCoastsSelection(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        var selection = _renderControl.SelectedSelection ?? new TileSelection(tile.X, tile.Y, tile.X, tile.Y);
        Destructive("corregir costas", () => _ = _session.FixCoasts(selection));
    }

    private void ApplyBlockMask(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        using var dialog = new Form { Text = "Bloqueo por lados", Width = 320, Height = 240, StartPosition = FormStartPosition.CenterParent };
        var sides = new[] { "Norte (1)", "Este (2)", "Sur (4)", "Oeste (8)" };
        var checks = sides.Select(text => new CheckBox { Text = text, Dock = DockStyle.Top, Checked = true }).ToArray();
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept);
        for (var index = checks.Length - 1; index >= 0; index--) dialog.Controls.Add(checks[index]);
        dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        byte mask = 0;
        for (var index = 0; index < checks.Length; index++) if (checks[index].Checked) mask |= (byte)(1 << index);
        if (_session.CurrentWorld is not null && _renderControl.SelectedGlobalSelection is { } globalSelection)
        {
            Destructive("aplicar bloqueo global por lados", () => _ = _session.ApplyGlobalBlockMask(globalSelection, mask));
            return;
        }
        var selection = _renderControl.SelectedSelection ?? new TileSelection(tile.X, tile.Y, tile.X, tile.Y);
        Destructive("aplicar bloqueo por lados", () => _ = _session.ApplyBlockMask(selection, mask));
    }

    private void ClearSelectedEntity(bool clearObject, bool clearNpc, bool clearTrigger, bool clearTransfer)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        var description = clearObject ? "quitar objeto" : clearNpc ? "quitar NPC" : clearTrigger ? "quitar trigger" : "quitar traslado";
        Destructive(description, () => _session.ClearSelectedEntity(tile.X, tile.Y, clearObject, clearNpc, clearTrigger, clearTransfer));
    }

    private void ClearSelectedGraphics(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        if (_session.CurrentWorld is not null && _renderControl.SelectedGlobalSelection is { } globalSelection)
        {
            Destructive("borrar las capas de la selección global", () => _ = _session.ClearGlobalSelectionGraphics(globalSelection));
            return;
        }
        Destructive("borrar todas las capas del tile", () => _session.ClearTileGraphics(tile.X, tile.Y));
    }

    private void ClearSelectedLight(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive("quitar luz del tile", () => { _session.ClearSelectedLight(tile.X, tile.Y); _renderControl.RefreshLightingAfterEdit(); });
    }

    private void ClearSelectedParticle(object? sender, EventArgs e)
    {
        if (!TryGetSelectedTile(out var tile)) return;
        Destructive("quitar partícula del tile", () => _session.ClearSelectedParticle(tile.X, tile.Y));
    }

    private void SelectGraphic(object? sender, EventArgs e)
    {
        var entries = new GraphicBrowserService().Enumerate(_session.Resources.Graphics);
        using var dialog = new Form { Text = "Listado de gráficos", Width = 480, Height = 620, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        list.Items.AddRange(entries.Select(entry => $"{entry.Index}  ({entry.Data.PixelWidth}x{entry.Data.PixelHeight})").Cast<object>().ToArray());
        var previewBox = new PictureBox { Dock = DockStyle.Top, Height = 180, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.Black };
        var preview = new Label { Text = "Seleccione un GRH", Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleLeft };
        void UpdatePreview()
        {
            if (list.SelectedIndex >= 0)
            {
                var entry = entries[list.SelectedIndex];
                preview.Text = $"GRH {entry.Index}: {entry.Data.PixelWidth}x{entry.Data.PixelHeight}, frames {entry.Data.Frames.Length}";
                previewBox.Image?.Dispose();
                try { previewBox.Image = new GraphicPreviewService(_session.ResourceRoot).Create(entry.Data); }
                catch { previewBox.Image = null; }
            }
        }
        list.SelectedIndexChanged += (_, _) => UpdatePreview();
        var accept = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dialog.DialogResult = DialogResult.OK; };
        dialog.FormClosed += (_, _) => previewBox.Image?.Dispose();
        dialog.Controls.Add(list); dialog.Controls.Add(preview); dialog.Controls.Add(previewBox); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
        if (_renderControl.SelectedGraphicIndex is > 0 and <= 200000)
        {
            list.SelectedIndex = entries.ToList().FindIndex(entry => entry.Index == _renderControl.SelectedGraphicIndex);
            UpdatePreview();
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (list.SelectedIndex < 0) return;
            _renderControl.SelectedGraphicIndex = entries[list.SelectedIndex].Index;
            _status.Text = $"Gráfico seleccionado: {_renderControl.SelectedGraphicIndex}";
        }
    }

    private void ExportMapPng(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = new SaveFileDialog { Filter = "Imagen PNG (*.png)|*.png|Imagen BMP (*.bmp)|*.bmp|Imagen JPEG (*.jpg)|*.jpg", FileName = Path.GetFileNameWithoutExtension(_session.CurrentMapPath ?? "mapa") + ".png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            new MapImageCaptureService().Save(_session.CurrentMap, _session.Resources.Graphics, _session.ResourceRoot, dialog.FileName,
                objects: _session.Resources.Objects);
            _status.Text = $"Mapa exportado: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Exportar mapa", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ConvertMaps(object? sender, EventArgs e)
    {
        using var sourceDialog = new FolderBrowserDialog { Description = "Carpeta con los mapas .map a convertir" };
        if (sourceDialog.ShowDialog(this) != DialogResult.OK) return;
        using var destinationDialog = new FolderBrowserDialog { Description = "Carpeta destino para los .csm" };
        if (destinationDialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var results = new MapConversionService().ConvertDirectory(sourceDialog.SelectedPath, destinationDialog.SelectedPath);
            var converted = results.Count(result => result.Converted);
            _status.Text = $"Conversión: {converted}/{results.Count} mapas";
            if (converted != results.Count)
                MessageBox.Show(this, $"No se pudieron convertir {results.Count - converted} mapas.", "Conversión de mapas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Conversión de mapas", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowAbout(object? sender, EventArgs e)
    {
        MessageBox.Show(this,
            "AO WorldEditor\nMigración del editor VB6 de Argentum Online a .NET 8 / WinForms.\nRenderer: D3D11 (Vortice).\nRecursos: solo lectura.",
            "Acerca de AO WorldEditor", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CopyMapImageToClipboard(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        try
        {
            using var image = new MapImageCaptureService().Capture(_session.CurrentMap, _session.Resources.Graphics, _session.ResourceRoot,
                objects: _session.Resources.Objects);
            Clipboard.SetImage(image);
            _status.Text = "Imagen del mapa copiada al portapapeles";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportAllMapsPng(object? sender, EventArgs e)
    {
        if (_session.CurrentMapPath is null) return;
        using var dialog = new FolderBrowserDialog { Description = "Carpeta destino para los PNG" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var results = new BatchMapExportService().ExportDirectory(Path.GetDirectoryName(_session.CurrentMapPath)!, dialog.SelectedPath,
                _session.Resources.Graphics, _session.ResourceRoot, _session.Resources.Objects, _session.Resources.Particles,
                _session.Resources.Npcs, _session.Resources.Bodies, _session.Resources.Heads,
                _session.Resources.Helmets, _session.Resources.Weapons, _session.Resources.Shields);
            var exported = results.Count(result => result.Exported);
            _status.Text = $"Exportación por lote: {exported}/{results.Count} mapas";
            if (exported != results.Count)
                MessageBox.Show(this, $"No se pudieron exportar {results.Count - exported} mapas.", "Exportación por lote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Exportación por lote", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportMiniMapPng(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = new SaveFileDialog { Filter = "Imagen PNG (*.png)|*.png", FileName = Path.GetFileNameWithoutExtension(_session.CurrentMapPath ?? "minimapa") + "-mini.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var render = _renderControl.RenderOptions;
            var options = new MapMiniMapOptions(render.ShowLayer1, render.ShowLayer2, render.ShowLayer3,
                render.ShowLayer4, render.ShowBlocked, render.ShowObjects, render.ShowNpcs, render.ShowParticles);
            new MapMiniMapService().SavePng(_session.CurrentMap, _session.Resources.Graphics, options,
                _session.Resources.Objects, dialog.FileName);
            _status.Text = $"Minimapa exportado: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Exportar minimapa", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PlaceBlock(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null || _renderControl.SelectedTile is not { } tile)
        {
            MessageBox.Show(this, "Cargue un mapa y seleccione un tile.", "Colocar bloque");
            return;
        }
        using var file = new OpenFileDialog { Filter = "Base de bloques (*.txt;*.ini)|*.txt;*.ini|Todos los archivos|*.*" };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var types = new BlockPresetLoader().Load(file.FileName);
            if (types.Count == 0) return;
            var typeIndex = PromptIndex("Tipo de bloque", types.Select((item, index) => $"{index + 1}: {item.Name}"), types.Count);
            if (typeIndex < 0) return;
            var type = types[typeIndex];
            var itemIndex = PromptIndex("Bloque", type.Items.Select((item, index) => $"{index + 1}: {item.Name} ({item.GraphicIndex})"), type.Items.Count);
            if (itemIndex < 0) return;
            Destructive("colocar el bloque", () => _ = _session.PlaceBlock(type, type.Items[itemIndex], tile.X, tile.Y));
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Error de bloques", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private int PromptIndex(string title, IEnumerable<string> entries, int count)
    {
        using var dialog = new Form { Text = title, Width = 420, Height = 150, StartPosition = FormStartPosition.CenterParent };
        var combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(entries.Cast<object>().ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        var accept = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(combo); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
        return dialog.ShowDialog(this) == DialogResult.OK && combo.SelectedIndex >= 0 ? combo.SelectedIndex : -1;
    }

    private void OptimizeMap(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = new Form { Text = "Optimizar mapa", Width = 460, Height = 290, StartPosition = FormStartPosition.CenterParent };
        var checks = new[]
        {
            new CheckBox { Text = "Quitar traslados bloqueados", Checked = true },
            new CheckBox { Text = "Quitar triggers bloqueados" },
            new CheckBox { Text = "Quitar triggers en traslados" },
            new CheckBox { Text = "Quitar NPCs, objetos y traslados en bordes" },
            new CheckBox { Text = "Mapear objetos especiales en capa 3" },
            new CheckBox { Text = "Bloquear objetos especiales", Checked = true }
        };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var check in checks) panel.Controls.Add(check);
        var accept = new Button { Text = "Optimizar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(panel); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var options = new MapOptimizationOptions(checks[0].Checked, checks[1].Checked, checks[2].Checked,
            checks[3].Checked, checks[4].Checked, checks[5].Checked);
        Destructive("optimizar el mapa", () => _session.Optimize(options));
    }

    private void EditMapInfo(object? sender, EventArgs e)
    {
        var map = _session.CurrentMap;
        if (map is null) return;
        using var dialog = new Form { Text = "Información del mapa", Width = 500, Height = 430, StartPosition = FormStartPosition.CenterParent };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 0, Padding = new Padding(8), AutoScroll = true };
        var fields = new Dictionary<string, TextBox>(StringComparer.Ordinal)
        {
            ["Nombre"] = new TextBox { Text = map.Info.Name }, ["Música"] = new TextBox { Text = map.Info.Music },
            ["Versión"] = new TextBox { Text = map.Info.MapVersion.ToString() }, ["Terreno"] = new TextBox { Text = map.Info.Terrain },
            ["Zona"] = new TextBox { Text = map.Info.Zone }, ["Restricción"] = new TextBox { Text = map.Info.Restriction },
            ["Luz"] = new TextBox { Text = map.Info.Light }
        };
        foreach (var field in fields)
        { panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(new Label { Text = field.Key, AutoSize = true }, 0, panel.RowCount); panel.Controls.Add(field.Value, 1, panel.RowCount++); }
        var pk = new CheckBox { Text = "PK permitido", Checked = map.Info.PlayerKilling, AutoSize = true };
        var magic = new CheckBox { Text = "Magia sin efecto", Checked = map.Info.MagicDisabled != 0, AutoSize = true };
        var invisibility = new CheckBox { Text = "Invisibilidad sin efecto", Checked = map.Info.InvisibilityDisabled != 0, AutoSize = true };
        var resurrection = new CheckBox { Text = "Resurrección sin efecto", Checked = map.Info.ResurrectionDisabled != 0, AutoSize = true };
        var encrypt = new CheckBox { Text = "No encriptar mapa", Checked = map.Info.NoEncryptMap != 0, AutoSize = true };
        var backup = new CheckBox { Text = "Backup", Checked = map.Info.Backup != 0, AutoSize = true };
        foreach (var check in new[] { pk, magic, invisibility, resurrection, encrypt, backup }) { panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(check, 0, panel.RowCount++); panel.SetColumnSpan(check, 2); }
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(panel); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!short.TryParse(fields["Versión"].Text, out var version)) { MessageBox.Show(this, "La versión debe ser un entero.", "Información del mapa"); return; }
        var values = new MapInfoValues(fields["Nombre"].Text, fields["Música"].Text, version, pk.Checked,
            (byte)(magic.Checked ? 1 : 0), (byte)(invisibility.Checked ? 1 : 0), (byte)(resurrection.Checked ? 1 : 0),
            (byte)(encrypt.Checked ? 1 : 0), fields["Terreno"].Text, fields["Zona"].Text, fields["Restricción"].Text,
            (byte)(backup.Checked ? 1 : 0), fields["Luz"].Text);
        Destructive("actualizar la información del mapa", () => _session.ApplyMapInfo(values));
    }

    private void ConfigureAutoSave(object? sender, EventArgs e)
    {
        using var dialog = new Form { Text = "Guardar mapa automáticamente", Width = 360, Height = 170, StartPosition = FormStartPosition.CenterParent };
        var value = new NumericUpDown { Minimum = AutoSaveService.MinimumMinutes, Maximum = AutoSaveService.MaximumMinutes, Value = Math.Clamp(_autoSaveMinutes == 0 ? 10 : _autoSaveMinutes, AutoSaveService.MinimumMinutes, AutoSaveService.MaximumMinutes), Dock = DockStyle.Top };
        var accept = new Button { Text = "Aceptar y aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        var stop = new Button { Text = "Detener", DialogResult = DialogResult.No, Dock = DockStyle.Bottom };
        dialog.Controls.Add(value); dialog.Controls.Add(stop); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.No) { _autoSaveTimer.Stop(); _autoSaveMinutes = 0; _status.Text = "Auto-guardado detenido"; return; }
        if (result != DialogResult.OK) return;
        _autoSaveMinutes = (int)value.Value;
        _autoSaveTimer.Interval = _autoSaveMinutes * 60 * 1000;
        _autoSaveTimer.Start();
        _status.Text = $"Auto-guardado cada {_autoSaveMinutes} minutos";
    }

    private void RefreshIndices(object? sender, EventArgs e)
    {
        try
        {
            var result = _session.RefreshResourceIndices();
            if (_session.CurrentMap is not null) _renderControl.SetMap(_session.CurrentMap, _session.Resources);
            _status.Text = $"Índices actualizados: {result.Graphics} gráficos, {result.Objects} objetos, {result.Npcs} NPCs";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Error al actualizar índices", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowReport(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        var choices = Enum.GetValues<MapReportKind>();
        var index = PromptIndex("Tipo de informe", choices.Select(item => item.ToString()), choices.Length);
        if (index < 0) return;
        var report = new MapReportService().Generate(_session.CurrentMap, choices[index], _session.Resources.Objects, _session.Resources.Npcs);
        using var dialog = new Form { Text = "Informe", Width = 720, Height = 600, StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Text = report };
        dialog.Controls.Add(text); dialog.ShowDialog(this);
    }

    private void ReplaceValues(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = new Form { Text = "Reemplazar valores", Width = 420, Height = 240, StartPosition = FormStartPosition.CenterParent };
        var kind = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        kind.Items.AddRange(Enum.GetNames<MapReplacementKind>()); kind.SelectedIndex = 0;
        var from = new NumericUpDown { Minimum = 0, Maximum = 200000, Dock = DockStyle.Top };
        var to = new NumericUpDown { Minimum = 0, Maximum = 200000, Dock = DockStyle.Top };
        var fromLayer = new NumericUpDown { Minimum = 0, Maximum = 3, Dock = DockStyle.Top };
        var toLayer = new NumericUpDown { Minimum = 0, Maximum = 3, Dock = DockStyle.Top };
        var allMaps = new CheckBox { Text = "Aplicar a todos los mapas de la carpeta", Dock = DockStyle.Top };
        var sameGrh = new CheckBox { Text = "Usar el mismo GRH de origen", Dock = DockStyle.Top };
        var accept = new Button { Text = "Reemplazar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(sameGrh); dialog.Controls.Add(allMaps); dialog.Controls.Add(toLayer); dialog.Controls.Add(fromLayer); dialog.Controls.Add(to); dialog.Controls.Add(from); dialog.Controls.Add(kind); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var selected = (MapReplacementKind)kind.SelectedIndex;
        if (sameGrh.Checked && selected is MapReplacementKind.Graphic) to.Value = from.Value;
        if (allMaps.Checked)
        {
            if (_session.CurrentMapPath is null) return;
            if (MessageBox.Show(this, "¿Está seguro de reemplazar valores en todos los mapas de la carpeta?", "Reemplazo multi-mapa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                var results = new MultiMapReplacementService().ReplaceDirectory(Path.GetDirectoryName(_session.CurrentMapPath)!, selected,
                    (int)from.Value, (int)to.Value, (int)fromLayer.Value, (int)toLayer.Value, _session.Resources.Objects, _session.Resources.Npcs);
                _session.LoadMap(_session.CurrentMapPath);
                _renderControl.SetMap(_session.CurrentMap, _session.Resources);
                _status.Text = $"Reemplazo multi-mapa: {results.Sum(result => result.Changed)} cambios en {results.Count} mapas";
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Reemplazo multi-mapa", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        Destructive("reemplazar valores", () =>
        {
            _ = selected switch
            {
                MapReplacementKind.Graphic => _session.ReplaceGraphic((int)fromLayer.Value, (int)toLayer.Value, (int)from.Value, (int)to.Value),
                MapReplacementKind.Object => _session.ReplaceObject((short)from.Value, (short)to.Value),
                MapReplacementKind.Npc => _session.ReplaceNpc((short)from.Value, (short)to.Value),
                _ => _session.ReplaceTrigger((short)from.Value, (short)to.Value)
            };
        });
    }

    private void LookupGraphic(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new Form { Text = "GRH a BMP", Width = 320, Height = 150, StartPosition = FormStartPosition.CenterParent };
            var value = new NumericUpDown { Minimum = 1, Maximum = Math.Max(1, _session.Resources.Graphics.Count - 1), Dock = DockStyle.Top };
            var accept = new Button { Text = "Consultar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
            dialog.Controls.Add(value); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var bmp = new GraphicLookupService().ResolveBmp(_session.Resources.Graphics, (int)value.Value);
            MessageBox.Show(this, $"GRH {(int)value.Value} pertenece al BMP {bmp}.", "GRH a BMP");
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "GRH a BMP", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LookupGraphicInfo(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new Form { Text = "Información del GRH", Width = 320, Height = 150, StartPosition = FormStartPosition.CenterParent };
            var value = new NumericUpDown { Minimum = 1, Maximum = Math.Max(1, _session.Resources.Graphics.Count - 1), Dock = DockStyle.Top };
            var accept = new Button { Text = "Consultar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
            dialog.Controls.Add(value); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var info = new GraphicInfoService().Get(_session.Resources.Graphics, (int)value.Value);
            MessageBox.Show(this, $"GRH {info.Index}\nBMP: {info.Bmp}\nTamaño: {info.Width}x{info.Height}\nFrames: {info.FrameCount}\nVelocidad: {info.Speed}\nMinimap: {info.MiniMapColor}", "Información del GRH");
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Información del GRH", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ConfigureRendering(object? sender, EventArgs e)
    {
        using var dialog = new Form { Text = "Opciones de visualización", Width = 360, Height = 390, StartPosition = FormStartPosition.CenterParent };
        var options = _renderControl.RenderOptions;
        var checks = new[]
        {
            new CheckBox { Text = "Capa 1", Checked = options.ShowLayer1 }, new CheckBox { Text = "Capa 2", Checked = options.ShowLayer2 },
            new CheckBox { Text = "Capa 3", Checked = options.ShowLayer3 }, new CheckBox { Text = "Capa 4", Checked = options.ShowLayer4 },
            new CheckBox { Text = "Grilla", Checked = options.ShowGrid }, new CheckBox { Text = "Bloqueos", Checked = options.ShowBlocked },
            new CheckBox { Text = "Triggers", Checked = options.ShowTriggers }, new CheckBox { Text = "Traslados", Checked = options.ShowTransfers },
            new CheckBox { Text = "Objetos", Checked = options.ShowObjects }, new CheckBox { Text = "NPCs", Checked = options.ShowNpcs },
            new CheckBox { Text = "Partículas", Checked = options.ShowParticles }, new CheckBox { Text = "Luces", Checked = options.ShowLights }
        };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var check in checks) panel.Controls.Add(check);
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(panel); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        options.ShowLayer1 = checks[0].Checked; options.ShowLayer2 = checks[1].Checked; options.ShowLayer3 = checks[2].Checked; options.ShowLayer4 = checks[3].Checked;
        options.ShowGrid = checks[4].Checked; options.ShowBlocked = checks[5].Checked; options.ShowTriggers = checks[6].Checked; options.ShowTransfers = checks[7].Checked;
        options.ShowObjects = checks[8].Checked; options.ShowNpcs = checks[9].Checked; options.ShowParticles = checks[10].Checked; options.ShowLights = checks[11].Checked;
        if (_session.CurrentMap is not null) _renderControl.SetMap(_session.CurrentMap, _session.Resources);
    }

    private void ValidateMap(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        var validator = new MapValidationService();
        var issues = validator.Validate(_session.CurrentMap, _session.Resources.Objects, _session.Resources.Npcs);
        using var dialog = new Form { Text = "Errores del mapa", Width = 720, Height = 600, StartPosition = FormStartPosition.CenterParent };
        dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Text = validator.Format(issues) });
        dialog.ShowDialog(this);
    }

    private void ValidateAllMaps(object? sender, EventArgs e)
    {
        if (_session.CurrentMapPath is null) return;
        try
        {
            var report = new MultiMapValidationService().ValidateDirectory(Path.GetDirectoryName(_session.CurrentMapPath)!, _session.Resources.Objects, _session.Resources.Npcs);
            using var dialog = new Form { Text = "Errores de todos los mapas", Width = 800, Height = 620, StartPosition = FormStartPosition.CenterParent };
            var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Text = report };
            var save = new Button { Text = "Guardar informe...", Dock = DockStyle.Bottom };
            save.Click += (_, _) =>
            {
                using var file = new SaveFileDialog { Filter = "Informe de texto (*.txt)|*.txt", FileName = "errores-mapas.txt" };
                if (file.ShowDialog(dialog) == DialogResult.OK) File.WriteAllText(file.FileName, report);
            };
            dialog.Controls.Add(text); dialog.Controls.Add(save); dialog.ShowDialog(this);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Validación multi-mapa", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ConfigureAdjacentTransfers(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        var preset = PromptIndex("Perfil de traslados", new[] { "Básico", "Legal", "Ullathorpe" }, 3);
        if (preset < 0) return;
        var bounds = preset switch
        {
            1 => AdjacentTransferService.LegalBounds,
            2 => AdjacentTransferService.UllathorpeBounds,
            _ => AdjacentTransferService.BasicBounds
        };
        var detected = new AdjacentTransferService().DetectExistingMaps(_session.CurrentMap, bounds);
        using var dialog = new Form { Text = "Traslados adyacentes", Width = 360, Height = 300, StartPosition = FormStartPosition.CenterParent };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(8) };
        var names = new[] { "Arriba", "Derecha", "Abajo", "Izquierda" };
        var maps = new NumericUpDown[4]; var apply = new CheckBox[4];
        for (var i = 0; i < 4; i++)
        {
            apply[i] = new CheckBox { Text = names[i], Checked = detected[i] != 0, AutoSize = true };
            maps[i] = new NumericUpDown { Minimum = 0, Maximum = 1024, Value = detected[i], Dock = DockStyle.Fill };
            panel.Controls.Add(apply[i], 0, i); panel.Controls.Add(maps[i], 1, i);
        }
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(panel); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var transfers = new[]
        {
            new AdjacentTransfer(apply[0].Checked, (short)maps[0].Value, 0, bounds.TopTargetY, true, bounds.TopY, (short)(bounds.LeftX + 1), (short)(bounds.RightX - 1)),
            new AdjacentTransfer(apply[1].Checked, (short)maps[1].Value, bounds.LeftTargetX, 0, false, bounds.RightX, (short)(bounds.TopY + 1), (short)(bounds.BottomY - 1)),
            new AdjacentTransfer(apply[2].Checked, (short)maps[2].Value, 0, bounds.BottomTargetY, true, bounds.BottomY, (short)(bounds.LeftX + 1), (short)(bounds.RightX - 1)),
            new AdjacentTransfer(apply[3].Checked, (short)maps[3].Value, bounds.RightTargetX, 0, false, bounds.LeftX, (short)(bounds.TopY + 1), (short)(bounds.BottomY - 1))
        };
        Destructive("aplicar traslados adyacentes", () => _ = _session.ApplyAdjacentTransfers(transfers));
    }

    private void CopyBorderToNeighbor(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        var side = PromptIndex("Borde a copiar", new[] { "Norte", "Sur", "Este", "Oeste" }, 4);
        if (side < 0) return;
        using var file = new OpenFileDialog { Filter = "Mapas (*.csm;*.map)|*.csm;*.map|Todos los archivos|*.*", InitialDirectory = Path.GetDirectoryName(_session.CurrentMapPath) };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        var (region, x, y) = side switch
        {
            0 => (new MapRegion(1, 11, 100, 22), 1, 91),
            1 => (new MapRegion(1, 81, 100, 90), 1, 1),
            2 => (new MapRegion(76, 1, 87, 100), 1, 1),
            _ => (new MapRegion(13, 1, 25, 100), 88, 1)
        };
        try
        {
            var copied = _session.CopyRegionToMap(region, file.FileName, x, y);
            _status.Text = $"Borde {new[] { "Norte", "Sur", "Este", "Oeste" }[side]} copiado: {copied} tiles";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Copia de borde", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void StitchAdjacentMaps(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = new Form { Text = "Completar mapas vecinos", Width = 360, Height = 240, StartPosition = FormStartPosition.CenterParent };
        var checks = Enum.GetValues<StitchSide>().Select(side => new CheckBox { Text = side.ToString(), Checked = true, AutoSize = true }).ToArray();
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var check in checks) panel.Controls.Add(check);
        var accept = new Button { Text = "Completar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(panel); dialog.Controls.Add(accept); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var sides = Enum.GetValues<StitchSide>().Where((_, index) => checks[index].Checked).ToArray();
        try
        {
            var results = _session.StitchAdjacentMaps(sides);
            _status.Text = string.Join(" | ", results.Select(result => result.Found ? $"{result.Side}: mapa {result.Map}, {result.Copied} tiles" : $"{result.Side}: mapa {result.Map} no encontrado"));
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Mapas vecinos", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ConfigureEnvironment(object? sender, EventArgs e)
    {
        var map = _session.CurrentMap;
        if (map is null) return;
        using var dialog = new Form { Text = "Clima y luz base", Width = 330, Height = 220, StartPosition = FormStartPosition.CenterParent };
        var rain = new NumericUpDown { Minimum = 0, Maximum = 255, Value = map.Environment.Rain, Dock = DockStyle.Top };
        var snow = new NumericUpDown { Minimum = 0, Maximum = 255, Value = map.Environment.Snow, Dock = DockStyle.Top };
        var fog = new NumericUpDown { Minimum = 0, Maximum = 255, Value = map.Environment.Fog, Dock = DockStyle.Top };
        var light = new NumericUpDown { Minimum = int.MinValue, Maximum = int.MaxValue, Value = Math.Clamp(map.Environment.BaseLight, int.MinValue, int.MaxValue), Dock = DockStyle.Top };
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(light); dialog.Controls.Add(fog); dialog.Controls.Add(snow); dialog.Controls.Add(rain); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Destructive("actualizar clima y luz base", () => _session.ApplyEnvironment((byte)rain.Value, (byte)snow.Value, (byte)fog.Value, (int)light.Value));
    }

    private void PlaceAmbientation(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        try
        {
            var ambientiations = new Infrastructure.AmbientationLoader().Load(_session.ResourceRoot);
            var catalog = new AmbientationCatalogService();
            var types = catalog.Types(ambientiations);
            if (types.Count == 0) return;
            var typeChoice = PromptIndex("Tipo de ambientación", types.Select(type => $"Tipo {type}"), types.Count);
            if (typeChoice < 0) return;
            var filtered = catalog.FilterByType(ambientiations, types[typeChoice]);
            if (filtered.Count == 0) return;
            var selected = PromptIndex("Ambientación", filtered.Select((entry, index) => $"{index + 1}: {entry.Ambientation.Name} ({entry.Ambientation.GraphicIndex})"), filtered.Count);
            if (selected < 0) return;
            using var dialog = new Form { Text = "Colocar ambientación", Width = 330, Height = 220, StartPosition = FormStartPosition.CenterParent };
            var count = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = 5, Dock = DockStyle.Top };
            var layer = new NumericUpDown { Minimum = 1, Maximum = 4, Value = 1, Dock = DockStyle.Top };
            var blocked = new CheckBox { Text = "Bloquear tiles", Checked = false, Dock = DockStyle.Top };
            var accept = new Button { Text = "Colocar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
            dialog.Controls.Add(accept); dialog.Controls.Add(blocked); dialog.Controls.Add(layer); dialog.Controls.Add(count); dialog.AcceptButton = accept;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            Destructive("colocar ambientación", () => _ = _session.PlaceAmbientation(filtered[selected].Ambientation, (int)count.Value, (int)layer.Value, blocked.Checked));
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Ambientaciones", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PlaceRandomNpcs(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null || _session.Resources.Npcs.Count <= 1) return;
        using var dialog = new Form { Text = "NPCs al azar", Width = 360, Height = 190, StartPosition = FormStartPosition.CenterParent };
        var npc = new NumericUpDown { Minimum = 1, Maximum = _session.Resources.Npcs.Count - 1, Dock = DockStyle.Top };
        var count = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 10, Dock = DockStyle.Top };
        var accept = new Button { Text = "Colocar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(count); dialog.Controls.Add(npc); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK) Destructive("colocar NPCs al azar", () => _ = _session.PlaceRandomNpcs(new RandomNpcPlacementOptions((int)count.Value, (short)npc.Value)));
    }

    private void PlaceRandomObjects(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null || _session.Resources.Objects.Count <= 1) return;
        using var dialog = new Form { Text = "Objetos al azar", Width = 360, Height = 220, StartPosition = FormStartPosition.CenterParent };
        var item = new NumericUpDown { Minimum = 1, Maximum = _session.Resources.Objects.Count - 1, Dock = DockStyle.Top };
        var count = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 10, Dock = DockStyle.Top };
        var blocked = new CheckBox { Text = "Bloquear objetos", Dock = DockStyle.Top };
        var accept = new Button { Text = "Colocar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(blocked); dialog.Controls.Add(count); dialog.Controls.Add(item); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK) Destructive("colocar objetos al azar", () => _ = _session.PlaceRandomObjects(new RandomObjectPlacementOptions((int)count.Value, (short)item.Value, blocked.Checked)));
    }

    private Form CreateSurfaceDialog(string title, out NumericUpDown graphic, out NumericUpDown layer, out NumericUpDown count, out CheckBox blocked, out CheckBox mosaic, out NumericUpDown mosaicWidth, out NumericUpDown mosaicHeight)
    {
        var dialog = new Form { Text = title, Width = 380, Height = 340, StartPosition = FormStartPosition.CenterParent };
        graphic = new NumericUpDown { Minimum = 0, Maximum = Math.Max(1, _session.Resources.Graphics.Count - 1), Value = Math.Clamp(_renderControl.SelectedGraphicIndex, 0, Math.Max(1, _session.Resources.Graphics.Count - 1)), Dock = DockStyle.Top };
        layer = new NumericUpDown { Minimum = 1, Maximum = 4, Value = _renderControl.SelectedLayer + 1, Dock = DockStyle.Top };
        count = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 10, Dock = DockStyle.Top };
        blocked = new CheckBox { Text = "Bloquear tiles", Dock = DockStyle.Top };
        mosaic = new CheckBox { Text = "Mosaico", Checked = _mosaicEnabled, Dock = DockStyle.Top };
        mosaicWidth = new NumericUpDown { Minimum = 1, Maximum = 64, Value = Math.Max(1, _mosaicWidth), Dock = DockStyle.Top };
        var widthBox = mosaicWidth = new NumericUpDown { Minimum = 1, Maximum = 64, Value = Math.Max(1, _mosaicWidth), Dock = DockStyle.Top };
        var heightBox = mosaicHeight = new NumericUpDown { Minimum = 1, Maximum = 64, Value = Math.Max(1, _mosaicHeight), Dock = DockStyle.Top };
        var widthUp = new Button { Text = "Ancho +", Height = 28, Dock = DockStyle.Top };
        var widthDown = new Button { Text = "Ancho -", Height = 28, Dock = DockStyle.Top };
        var heightUp = new Button { Text = "Largo +", Height = 28, Dock = DockStyle.Top };
        var heightDown = new Button { Text = "Largo -", Height = 28, Dock = DockStyle.Top };
        widthUp.Click += (_, _) => widthBox.Value = Math.Min(widthBox.Maximum, widthBox.Value + 1);
        widthDown.Click += (_, _) => widthBox.Value = Math.Max(widthBox.Minimum, widthBox.Value - 1);
        heightUp.Click += (_, _) => heightBox.Value = Math.Min(heightBox.Maximum, heightBox.Value + 1);
        heightDown.Click += (_, _) => heightBox.Value = Math.Max(heightBox.Minimum, heightBox.Value - 1);
        var accept = new Button { Text = "Aplicar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(heightDown); dialog.Controls.Add(heightUp); dialog.Controls.Add(widthDown); dialog.Controls.Add(widthUp);
        dialog.Controls.Add(mosaicHeight); dialog.Controls.Add(mosaicWidth); dialog.Controls.Add(mosaic); dialog.Controls.Add(blocked); dialog.Controls.Add(count); dialog.Controls.Add(layer); dialog.Controls.Add(graphic); dialog.AcceptButton = accept;
        return dialog;
    }

    private void PlaceRandomSurface(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = CreateSurfaceDialog("Superficie al azar", out var graphic, out var layer, out var count, out var blocked, out var mosaic, out var mosaicWidth, out var mosaicHeight);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Destructive("colocar superficie al azar", () => _ = _session.PlaceRandomSurface(new RandomSurfaceOptions((int)count.Value, (int)layer.Value - 1, (int)graphic.Value, mosaic.Checked, (int)mosaicWidth.Value, (int)mosaicHeight.Value, mosaic.Checked, blocked.Checked)));
    }

    private void PlaceSurfaceBorders(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = CreateSurfaceDialog("Superficie en bordes", out var graphic, out var layer, out _, out var blocked, out var mosaic, out var mosaicWidth, out var mosaicHeight);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Destructive("colocar superficie en bordes", () => _ = _session.PlaceSurfaceOnBorders((int)layer.Value - 1, (int)graphic.Value, mosaic.Checked, (int)mosaicWidth.Value, (int)mosaicHeight.Value, blocked.Checked));
    }

    private void PlaceSurfaceWholeMap(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = CreateSurfaceDialog("Superficie en todo el mapa", out var graphic, out var layer, out _, out _, out var mosaic, out var mosaicWidth, out var mosaicHeight);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Destructive("colocar superficie en todo el mapa", () => _ = _session.PlaceSurfaceOnWholeMap((int)layer.Value - 1, (int)graphic.Value, mosaic.Checked, (int)mosaicWidth.Value, (int)mosaicHeight.Value));
    }

    private void ApplyTerrainPreset(int surfaceIndex)
    {
        try
        {
            var lookup = new SurfaceCatalogService().DameGrhIndex(_session.Resources.Surfaces, surfaceIndex);
            _renderControl.SelectedGraphicIndex = lookup.GraphicIndex;
            _mosaicEnabled = lookup.Mosaic;
            _mosaicWidth = Math.Max(1, (int)lookup.Width);
            _mosaicHeight = Math.Max(1, (int)lookup.Height);
            _terrainBlocked = lookup.Blocked;
            _terrainPresetSelected = true;
            _status.Text = $"Terreno #{lookup.SurfaceIndex}: GRH {lookup.GraphicIndex}" + (lookup.Mosaic ? $" | mosaico {lookup.Width}x{lookup.Height}" : string.Empty);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Terrenos", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PlaceRandomTrees(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null || _session.Resources.Objects.Count <= 1) return;
        using var dialog = new Form { Text = "Árboles al azar", Width = 360, Height = 190, StartPosition = FormStartPosition.CenterParent };
        var item = new NumericUpDown { Minimum = 1, Maximum = _session.Resources.Objects.Count - 1, Dock = DockStyle.Top };
        var count = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = 10, Dock = DockStyle.Top };
        var accept = new Button { Text = "Colocar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(count); dialog.Controls.Add(item); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Destructive("colocar árboles al azar", () => _ = _session.PlaceTrees(new TreePlacementOptions((int)count.Value, [(short)item.Value])));
    }

    private void SelectMusic(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        var catalog = new AudioCatalog(); catalog.Load(_session.ResourceRoot);
        string? selectedPath = null;
        if (catalog.MusicFiles.Count > 0)
        {
            var index = PromptIndex("Música MIDI", catalog.MusicFiles.Select(path => Path.GetFileName(path) ?? path), catalog.MusicFiles.Count);
            if (index >= 0) selectedPath = catalog.MusicFiles[index];
        }
        else
        {
            using var dialog = new OpenFileDialog { Filter = "Música MIDI (*.mid;*.midi)|*.mid;*.midi|Todos los archivos|*.*", InitialDirectory = _session.ResourceRoot };
            if (dialog.ShowDialog(this) == DialogResult.OK) selectedPath = dialog.FileName;
        }
        if (selectedPath is null) return;
        _session.SetMusic(Path.GetFileNameWithoutExtension(selectedPath));
        _status.Text = $"Música seleccionada: {_session.CurrentMap.Info.Music}";
    }

    private void PlayAmbientWave(object? sender, EventArgs e)
    {
        var catalog = new AudioCatalog(); catalog.Load(_session.ResourceRoot);
        string? selectedPath = null;
        if (catalog.AmbientWaveFiles.Count > 0)
        {
            var index = PromptIndex("WAV ambiental", catalog.AmbientWaveFiles.Select(path => Path.GetFileName(path) ?? path), catalog.AmbientWaveFiles.Count);
            if (index >= 0) selectedPath = catalog.AmbientWaveFiles[index];
        }
        else
        {
            using var dialog = new OpenFileDialog { Filter = "Audio WAV (*.wav)|*.wav|Todos los archivos|*.*", InitialDirectory = _session.ResourceRoot };
            if (dialog.ShowDialog(this) == DialogResult.OK) selectedPath = dialog.FileName;
        }
        if (selectedPath is null) return;
        try { _audioPlayback.PlayWave(selectedPath); _status.Text = $"Reproduciendo: {Path.GetFileName(selectedPath)}"; }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Audio ambiental", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PlayMidi(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "Música MIDI (*.mid;*.midi)|*.mid;*.midi|Todos los archivos|*.*", InitialDirectory = _session.ResourceRoot };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _audioPlayback.PlayMidi(dialog.FileName); _status.Text = $"Reproduciendo MIDI: {Path.GetFileName(dialog.FileName)}"; }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Música MIDI", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void StopAudio(object? sender, EventArgs e)
    {
        _audioPlayback.Stop();
        _status.Text = "Audio detenido";
    }

    private void AutoSaveTick()
    {
        try
        {
            var root = EditorPaths.AutosaveDirectory;
            var path = _session.AutoSave(_autoSaveMinutes, root);
            if (path is not null) _status.Text = $"Auto-guardado: {Path.GetFileName(path)}";
        }
        catch (Exception error) { _status.Text = "Error de auto-guardado: " + error.Message; }
    }

    private void Destructive(string description, Action action)
    {
        if (_session.CurrentMap is null) return;
        if (MessageBox.Show(this, $"¿Confirmar {description}?", "Confirmar operación", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        try
        {
            action();
            _renderControl.RefreshWorldView();
            _status.Text = $"Operacion completada: {description}" +
                (_session.UndoOmitted ? " | undo omitido: seleccion muy grande" : string.Empty);
            _inspector.AddHistory(description);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Error de edicion", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveMap(object? sender, EventArgs e)
    {
        if (_session.CurrentWorld is not null)
        {
            try
            {
                var result = _session.SaveWorld();
                _status.Text = $"Mundo guardado: {result.Saved.Count} sectores" +
                    (result.Errors.Count == 0 ? string.Empty : $" | {result.Errors.Count} errores");
                if (result.Errors.Count > 0)
                    MessageBox.Show(this, string.Join(Environment.NewLine, result.Errors.Take(20)),
                        "Mapas no guardados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Error al guardar mundo", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        if (_session.CurrentMap is null)
        {
            MessageBox.Show(this, "No hay un mapa cargado.", "Guardar mapa");
            return;
        }
        if (_session.CurrentMapPath is not null)
        {
            try { _session.SaveMap(_session.CurrentMapPath); _status.Text = $"Mapa guardado: {Path.GetFileName(_session.CurrentMapPath)}"; }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Error al guardar mapa", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        SaveMapAs(sender, e);
    }

    private void SaveMapAs(object? sender, EventArgs e)
    {
        if (_session.CurrentMap is null) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "Mapa CSM (*.csm)|*.csm|Mapa legacy (*.map)|*.map",
            FileName = Path.GetFileName(_session.CurrentMapPath ?? "mapa.csm"),
            InitialDirectory = Path.GetDirectoryName(_session.CurrentMapPath) ?? _session.ResourceRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _session.SaveMap(dialog.FileName);
            _settings.LastMap = _session.CurrentMapPath ?? dialog.FileName;
            _status.Text = $"Mapa guardado: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Error al guardar mapa", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async void SaveGlobalWorldAs(object? sender, EventArgs e)
    {
        if (_globalSaveInProgress) return;
        var world = _session.CurrentWorld;
        if (world is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Guardar mundo global");
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "WORLD (*.csm)|*.csm|Todos los archivos (*.*)|*.*",
            InitialDirectory = Path.Combine(ClientResourceRoot, "world"),
            FileName = "world.csm",
            Title = "Guardar WORLD"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var output = ResolveGlobalOutputPaths(dialog.FileName);
            _globalSaveInProgress = true;
            UseWaitCursor = true;
            _status.Text = "Guardando WORLD y generando world.png...";
            var resources = _session.Resources;
            var resourceRoot = _session.ResourceRoot;
            await _miniMap.WaitForWorldImageAsync();
            var embeddedImages = CaptureWorldImageForSave();
            var result = await Task.Run(() =>
            {
                var path = new GlobalCsmArchiveService().Save(world, output.Csm, output.Dat, output.Inf, output.Map,
                    resources, resourceRoot, embeddedImages);
                var audit = new GlobalArchiveAuditService().Audit(path, world, output.Dat, output.Inf, output.Map);
                var semantic = new GlobalWorldSemanticAuditService().Audit(world, resources, path, output.Inf);
                return (path, audit, semantic);
            });
            var path = result.path;
            var audit = result.audit;
            var semantic = result.semantic;
            var auditStatus = audit.Errors + semantic.Errors == 0 ? "auditoría OK" : $"errores de auditoría: {audit.Errors + semantic.Errors}";
            var auditDetails = audit.Issues.Count == 0 && semantic.Issues.Count == 0
                ? "Auditoría: OK"
                : "Auditoría:" + Environment.NewLine + string.Join(Environment.NewLine,
                    audit.Issues.Select(issue => $"- {issue.Code}: {issue.Message}").Concat(
                        semantic.Issues.Select(issue => $"- {issue.Code}: mapa={issue.MapId} local={issue.LocalX},{issue.LocalY} global={issue.GlobalX},{issue.GlobalY}: {issue.Message}")));
            _status.Text = $"WORLD guardado: world.csm, world.dat y world.inf ({world.Paths.Count} mapas) | {auditStatus}";
            MessageBox.Show(this,
                $"Se guardaron los tres archivos globales con {world.Paths.Count} mapas y sus metadatos:{Environment.NewLine}{Environment.NewLine}" +
                $"{path}{Environment.NewLine}{output.Map}{Environment.NewLine}{output.Dat}{Environment.NewLine}{output.Inf}{Environment.NewLine}{Environment.NewLine}{auditDetails}",
                "WORLD guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al guardar WORLD", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _globalSaveInProgress = false;
        }
    }

    private async void SaveUnifiedWorldAs(object? sender, EventArgs e)
    {
        if (_session.CurrentWorld is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Compilar mapa único", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "Mapa global único (*.gmap)|*.gmap|Todos los archivos (*.*)|*.*",
            InitialDirectory = Path.Combine(ClientResourceRoot, "world"),
            FileName = "world.gmap",
            Title = "Compilar mapa único global"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            _status.Text = "Compilando mapa único global...";
            var result = await Task.Run(() => _session.SaveUnifiedWorld(dialog.FileName));
            // Keep the compiled artifact as the radar source. The radar itself is
            // cropped lazily from this grid; no legacy world bitmap is required.
            _compiledUnifiedWorld = result.Map;
            _radarDirty = true;
            var conflictText = result.Conflicts.Count == 0
                ? "sin conflictos"
                : $"{result.Conflicts.Count} conflictos";
            _status.Text = $"Mapa único generado: Z={result.Map.FloorZ} | {result.Map.Width}x{result.Map.Height} | {conflictText}";
            MessageBox.Show(this,
                $"Mapa global único generado:{Environment.NewLine}{Environment.NewLine}" +
                $"Archivo: {dialog.FileName}{Environment.NewLine}" +
                $"Dimensiones: {result.Map.Width} x {result.Map.Height}{Environment.NewLine}" +
                $"Tiles escritos: {result.WrittenTiles}{Environment.NewLine}" +
                $"Bordes fusionados: {result.MergedTiles}{Environment.NewLine}" +
                $"NPCs: {result.Npcs} | Objetos: {result.Objects} | Partículas: {result.Particles}{Environment.NewLine}" +
                $"Luces: {result.Lights} | Salidas internas: {result.InternalExits} | Externas: {result.ExternalExits}{Environment.NewLine}" +
                $"Resultado: {conflictText}",
                "Mapa único generado", MessageBoxButtons.OK,
                result.Conflicts.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al compilar mapa único", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private async void SaveUnifiedWorldBundleAs(object? sender, EventArgs e)
    {
        if (_session.CurrentWorld is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Compilar pisos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new FolderBrowserDialog
        {
            Description = "Seleccione la carpeta donde se guardará world.floors",
            SelectedPath = ClientResourceRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var directory = Path.Combine(dialog.SelectedPath, "world.floors");
        try
        {
            UseWaitCursor = true;
            _status.Text = "Generando world.floors desde los mapas originales...";
            var bundle = await Task.Run(() => _session.SaveUnifiedWorldBundle(directory, applyStitching: true));
            await OpenBundlePreviewDirectory(directory);
            _status.Text = $"world.floors guardado: {bundle.Floors.Count} pisos | Z=0 activo";
            MessageBox.Show(this,
                $"world.floors generado en:{Environment.NewLine}{directory}{Environment.NewLine}{Environment.NewLine}" +
                $"Pisos: {string.Join(", ", bundle.Floors.Keys.Select(z => $"Z={z}"))}",
                "world.floors guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al compilar pisos", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private async void GenerateUnifiedWorldConflictReport(object? sender, EventArgs e)
    {
        if (_session.CurrentWorld is null)
        {
            MessageBox.Show(this, "Primero abra el mundo completo.", "Informe global", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            UseWaitCursor = true;
            _status.Text = "Analizando conflictos del mundo global...";
            var bundle = await Task.Run(() => new UnifiedWorldCompiler().CompileFloors(_session.CurrentWorld, _session.Resources));
            var output = Path.Combine(ClientResourceRoot, "world");
            var report = new UnifiedWorldConflictReportService();
            report.Write(bundle, output);
            var count = report.Build(bundle).Count;
            _status.Text = $"Informe global generado: {count} conflictos";
            MessageBox.Show(this,
                $"Informe generado en:{Environment.NewLine}{output}{Environment.NewLine}{Environment.NewLine}" +
                "unified-world-conflicts.json\nunified-world-conflicts.csv",
                "Informe global", MessageBoxButtons.OK,
                count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al generar informe global", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private IReadOnlyList<GlobalResolvedWorldSnapshot.EmbeddedImage> CaptureWorldImageForSave()
    {
        var image = _miniMap.WorldImage
            ?? throw new InvalidOperationException("El mapa mundial todavía no terminó de componerse.");
        using var copy = new Bitmap(image);
        using var stream = new MemoryStream();
        copy.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return [new GlobalResolvedWorldSnapshot.EmbeddedImage("worldmap/world.png", stream.ToArray())];
    }

    private bool ConfirmPendingChanges()
    {
        var hasChanges = _session.CurrentWorld is not null
            ? _session.CurrentWorld.LoadedMaps.Any(item => item.Map.Info.Changed)
            : _session.CurrentMap?.Info.Changed == true;
        if (!hasChanges) return true;
        var answer = MessageBox.Show(this, "El mapa tiene cambios sin guardar. ¿Desea guardarlos antes de continuar?", "Mapa modificado", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes)
        {
            SaveMap(this, EventArgs.Empty);
            return _session.CurrentWorld is not null
                ? !_session.CurrentWorld.LoadedMaps.Any(item => item.Map.Info.Changed)
                : _session.CurrentMap?.Info.Changed != true;
        }
        return true;
    }

    private void NewMap(object? sender, EventArgs e)
    {
        if (!ConfirmPendingChanges()) return;
        _session.NewMap(); _settings.LastMap = string.Empty; _miniMap.SetWorld(null, null, () => _renderControl.GlobalViewport, _renderControl.CenterOnGlobalTile); _renderControl.SetMap(_session.CurrentMap!, _session.Resources); _status.Text = "Mapa nuevo";
    }

    private void ReopenMap(object? sender, EventArgs e)
    {
        if (_session.CurrentMapPath is null) return;
        if (!ConfirmPendingChanges()) return;
        try { _session.LoadMap(_session.CurrentMapPath); _settings.LastMap = _session.CurrentMapPath; _miniMap.SetWorld(null, null, () => _renderControl.GlobalViewport, _renderControl.CenterOnGlobalTile); _renderControl.SetMap(_session.CurrentMap!, _session.Resources); _status.Text = "Mapa reabierto"; }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Error al reabrir mapa", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenMap(object? sender, EventArgs e)
    {
        if (!ConfirmPendingChanges()) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Mapas Argentum Online (*.csm;*.map)|*.csm;*.map|Todos los archivos (*.*)|*.*",
            InitialDirectory = SourceMapsDirectory,
            Title = "Abrir mapa"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            _session.LoadMap(dialog.FileName);
            _settings.LastMap = _session.CurrentMapPath ?? dialog.FileName;
            UpdateTitle();
            var map = _session.CurrentMap!;
            _status.Text = $"Mapa: {Path.GetFileName(dialog.FileName)} | capas cargadas | recursos: {_session.Resources.Graphics.Count - 1} GRH";
            _miniMap.SetWorld(null, null, () => _renderControl.GlobalViewport, _renderControl.CenterOnGlobalTile);
            _renderControl.SetMap(map, _session.Resources);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al abrir mapa", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenUnifiedWorld(object? sender, EventArgs e)
    {
        if (!ConfirmPendingChanges()) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Mapa global único (*.gmap)|*.gmap|Todos los archivos (*.*)|*.*",
            InitialDirectory = Path.Combine(ClientResourceRoot, "world"),
            Title = "Abrir mapa único global"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var map = _session.LoadUnifiedWorld(dialog.FileName);
            _compiledUnifiedBundle = null;
            _compiledUnifiedWorld = map;
            _renderControl.SetUnifiedWorld(map, _session.Resources);
            _miniMap.SetWorld(null, null, () => _renderControl.GlobalViewport, _renderControl.CenterOnGlobalTile);
            _radarEnabled = true;
            _sidebar.SetRadarVisible(true);
            _radarTimer.Start();
            _radarDirty = true;
            _status.Text = $"Mapa único: Z={map.FloorZ} | {map.Width}x{map.Height} | {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al abrir mapa único", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OpenUnifiedWorldBundle(object? sender, EventArgs e)
    {
        if (!ConfirmPendingChanges()) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Seleccione la carpeta del bundle de pisos",
            SelectedPath = Path.Combine(ClientResourceRoot, "world.floors")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await OpenBundlePreviewDirectory(dialog.SelectedPath);
    }

    public async Task OpenBundlePreviewDirectory(string directory)
    {
        try
        {
            if (!File.Exists(Path.Combine(directory, "bundle.aog")))
                throw new InvalidDataException("La carpeta no contiene bundle.aog.");
            var images = Directory.EnumerateFiles(directory, "floor-*.gmap")
                .Select(path => (Path: path, Name: Path.GetFileNameWithoutExtension(path)))
                .Select(item => (item.Path, Parsed: int.TryParse(item.Name[6..], out var z), Z: int.TryParse(item.Name[6..], out var value) ? value : 0))
                .Where(item => item.Parsed)
                .ToDictionary(item => item.Z, item => item.Path);
            if (images.Count == 0) throw new InvalidDataException("El bundle no contiene floor-*.gmap.");
            _previewFloorImages = images;
            // Z=0 is the canonical starting floor. Fall back to the first
            // available image only for bundles that do not contain floor 0.
            _previewFloorZ = images.ContainsKey(0) ? 0 : images.Keys.OrderBy(z => z).First();
            UseWaitCursor = true;
            _status.Text = "Mostrando mundo completo; cargando datos del piso...";
            var overview = Path.Combine(directory, "worldmap", $"floor-{_previewFloorZ.Value:+#;-#;0}.png");
            if (File.Exists(overview))
                _renderControl.SetUnifiedWorldPreview(await Task.Run(() => LoadBitmapCopy(overview)), _previewFloorZ.Value);
            _radarEnabled = true;
            _sidebar.SetRadarVisible(true);
            _radarTimer.Start();
            var map = await Task.Run(() => _session.LoadUnifiedWorldFloor(directory, _previewFloorZ.Value));
            _compiledUnifiedBundle = null;
            _compiledUnifiedWorld = map;
            _renderControl.SetUnifiedWorld(map, _session.Resources);
            if (File.Exists(overview))
                _renderControl.SetUnifiedWorldPreview(await Task.Run(() => LoadBitmapCopy(overview)), _previewFloorZ.Value);
            _miniMap.SetWorld(null, null, () => _renderControl.GlobalViewport, _renderControl.CenterOnGlobalTile);
            _radarDirty = true;
            _status.Text = $"Bundle de pisos: Z={_previewFloorZ.Value} | {images.Count} pisos | diseño real";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al abrir bundle de pisos", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    public async Task RebuildBundleAsync(string directory)
    {
        UseWaitCursor = true;
        _status.Text = "Regenerando world.floors con stitching...";
        try
        {
            var source = Path.Combine(_session.ReferenceResourceRoot, "Mapas");
            await Task.Run(() =>
            {
                _session.LoadWorld(source);
                _session.SaveUnifiedWorldBundle(directory, applyStitching: true);
            });
            _status.Text = "world.floors regenerado con stitching.";
        }
        finally { UseWaitCursor = false; }
    }

    public async Task AuditStitchAsync(string directory)
    {
        UseWaitCursor = true;
        _status.Text = "Analizando stitching sin regenerar world.floors...";
        try
        {
            var source = Path.Combine(_session.ReferenceResourceRoot, "Mapas");
            await Task.Run(() =>
            {
                _session.LoadWorld(source);
                _session.SaveStitchAudits(directory);
            });
            _status.Text = "Auditoría de stitching guardada.";
        }
        finally { UseWaitCursor = false; }
    }

    public async Task AuditLooseTeleportsAsync(string directory)
    {
        UseWaitCursor = true;
        _status.Text = "Auditando teleports VB6 de mapas originales...";
        try
        {
            var maps = Path.Combine(_session.ReferenceResourceRoot, "Mapas");
            await Task.Run(() =>
            {
                var audit = new LooseMapTeleportAuditService();
                audit.SaveTsv(Path.Combine(directory, "loose-map-teleport-audit.tsv"), audit.Audit(maps));
            });
            _status.Text = "Audit de teleports de mapas originales guardado.";
        }
        finally { UseWaitCursor = false; }
    }

    public async Task CompileStitchedExperimentalAsync(string directory)
    {
        UseWaitCursor = true;
        _status.Text = "Generando bundle experimental compacto...";
        try
        {
            // The canonical source is the original loose CSM collection. The
            // old generated `world` archive is not part of the new runtime
            // pipeline and must not be used to rebuild world.floors.
            var source = Path.Combine(_session.ReferenceResourceRoot, "Mapas");
            await Task.Run(() =>
            {
                _session.LoadWorld(source);
                _session.SaveUnifiedWorldBundle(directory, applyStitching: true);
            });
            _status.Text = "Bundle experimental compacto generado.";
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "experimental-error.txt"), error.ToString());
            throw;
        }
        finally { UseWaitCursor = false; }
    }

    private async void ChangeUnifiedFloor(int delta)
    {
        if (_previewFloorImages is not null && _previewFloorZ is { } previewZ)
        {
            var previewFloors = _previewFloorImages.Keys.OrderBy(z => z).ToArray();
            var previewIndex = Array.IndexOf(previewFloors, previewZ) + delta;
            if (previewIndex < 0 || previewIndex >= previewFloors.Length) return;
            var nextZ = previewFloors[previewIndex];
            UseWaitCursor = true;
            try
            {
                var floorMap = await Task.Run(() => _session.LoadUnifiedWorldFloor(
                    Path.GetDirectoryName(_previewFloorImages[nextZ])!, nextZ));
                _previewFloorZ = nextZ;
                _compiledUnifiedWorld = floorMap;
                _renderControl.SetUnifiedWorld(floorMap, _session.Resources);
                var overview = Path.Combine(Path.GetDirectoryName(_previewFloorImages[nextZ])!, "worldmap",
                    $"floor-{nextZ:+#;-#;0}.png");
                if (File.Exists(overview))
                    _renderControl.SetUnifiedWorldPreview(await Task.Run(() => LoadBitmapCopy(overview)), nextZ);
                _radarEnabled = true;
                _sidebar.SetRadarVisible(true);
                _radarDirty = true;
                _status.Text = $"Piso activo: Z={nextZ} | diseño real";
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Error al abrir piso", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { UseWaitCursor = false; }
            return;
        }
        if (_compiledUnifiedBundle is null) return;
        var floors = _compiledUnifiedBundle.Floors.Keys.ToArray();
        if (floors.Length == 0 || _compiledUnifiedBundle.ActiveFloorZ is not { } active) return;
        var index = Array.IndexOf(floors, active) + delta;
        if (index < 0 || index >= floors.Length) return;
        var map = _compiledUnifiedBundle.Select(floors[index]);
        _compiledUnifiedWorld = map;
        _renderControl.SetUnifiedWorld(map, _session.Resources);
        _radarDirty = true;
        _status.Text = $"Piso activo: Z={map.FloorZ}";
    }

    private static Bitmap LoadBitmapCopy(string path)
    {
        using var source = new Bitmap(path);
        return new Bitmap(source);
    }

    private async void OpenWorld(object? sender, EventArgs e)
    {
        if (!ConfirmPendingChanges()) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Seleccione la carpeta que contiene los mapas .csm del mundo",
            SelectedPath = SourceMapsDirectory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            _compileFloorsAfterWorldLoad = false;
            return;
        }
        using var loading = new Form
        {
            Text = "Cargando mundo",
            Width = 420,
            Height = 92,
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ControlBox = false
        };
        var label = new Label { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 0), Text = "Preparando mapas..." };
        var bar = new ProgressBar { Dock = DockStyle.Bottom, Height = 16, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
        loading.Controls.Add(label); loading.Controls.Add(bar);
        var progress = new Progress<WorldLoadProgress>(update =>
        {
            var current = Math.Clamp(update.Current, 0, Math.Max(1, update.Total));
            bar.Value = Math.Clamp((int)Math.Round(current * 100d / Math.Max(1, update.Total)), 0, 100);
            label.Text = $"Cargando mapa {current}/{update.Total}: {update.FileName}" +
                (update.Error is null ? string.Empty : $"  (omitido: {update.Error})");
        });
        loading.Location = new Point(
            Location.X + Math.Max(0, (Width - loading.Width) / 2),
            Location.Y + Math.Max(0, (Height - loading.Height) / 2));
        GlobalWorldDocument world;
        loading.Show(this);
        try
        {
            world = await Task.Run(() => _session.LoadWorld(dialog.SelectedPath, null, progress));
            if (_compileFloorsAfterWorldLoad)
            {
                bar.Style = ProgressBarStyle.Marquee;
                bar.MarqueeAnimationSpeed = 24;
                label.Text = "Compilando world.floors con metadatos de reference\\Recursos...";
                var bundleDirectory = Path.Combine(ClientResourceRoot, "world.floors");
                await Task.Run(() => _session.SaveUnifiedWorldBundle(bundleDirectory, applyStitching: true));
                bar.Style = ProgressBarStyle.Continuous;
                bar.MarqueeAnimationSpeed = 0;
                bar.Value = 100;
                label.Text = "world.floors generado correctamente.";
                _compileFloorsAfterWorldLoad = false;
                await OpenBundlePreviewDirectory(bundleDirectory);
                _settings.LastMap = _session.CurrentMapPath ?? string.Empty;
                UpdateTitle();
                return;
            }
            _settings.LastMap = _session.CurrentMapPath ?? string.Empty;
            _renderControl.SetGlobalWorld(world, _session.Resources);
            _miniMap.SetWorld(world, _session.Resources.Graphics, () => _renderControl.GlobalViewport,
                _renderControl.CenterOnGlobalTile, _session.ResourceRoot);
            var connectedIds = world.Assembly.Placements.Select(item => item.MapId).ToHashSet();
            var disconnected = world.Maps.Keys.Count(mapId => !connectedIds.Contains(mapId));
            _status.Text = $"Mundo abierto: {world.Assembly.Placements.Count} sectores | " +
                $"{world.LoadedMapCount} en memoria | {world.Assembly.Conflicts.Count} conflictos | " +
                $"{disconnected} desconectados" +
                (world.IgnoredWorldIds.Count == 0 ? string.Empty :
                    $" | WORLDMAP ignorados: {string.Join(",", world.IgnoredWorldIds)}") +
                (world.LoadErrors.Count == 0 ? string.Empty : $" | mapas con error: {world.LoadErrors.Count}");
            UpdateTitle();
        }
        catch (Exception error)
        {
            loading.Close();
            MessageBox.Show(this, error.Message, "Error al abrir mundo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { loading.Close(); }
    }

    private void OpenOriginalMaps(object? sender, EventArgs e)
    {
        _compileFloorsAfterWorldLoad = true;
        OpenWorld(sender, e);
    }

    private async void RunFullAudit(object? sender, EventArgs e)
    {
        if (MessageBox.Show(this,
                "Audita los 843 CSM (formato, referencias, round-trip) y los 3 WORLDMAP " +
                "(coordenadas, cobertura, exits). Puede tardar un minuto. Continuar?",
                "Auditoría completa", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        using var loading = CreateProgressForm("Auditoría completa");
        var label = (Label)loading.Controls[0];
        var bar = (ProgressBar)loading.Controls[1];
        var progress = new Progress<AuditProgress>(update =>
        {
            label.Text = $"{update.Phase}: {update.File} ({update.Current}/{update.Total})";
            bar.Value = Math.Clamp((int)Math.Round(update.Current * 100d / Math.Max(1, update.Total)), 0, 100);
        });
        loading.Show(this);
        try
        {
            var maps = Path.Combine(_session.ResourceRoot, "Mapas");
            var result = await Task.Run(() => new WorldAuditService().Run(
                maps, _session.ResourceRoot, _session.Resources, progress));
            loading.Close();
            _status.Text = $"Auditoría: mapas E={result.MapErrors} W={result.MapWarnings} | mundos E={result.WorldErrors} | indices E={result.IndexErrors}";
            MessageBox.Show(this,
                $"Auditoría completa terminada.{Environment.NewLine}{Environment.NewLine}" +
                $"Mapas: {result.Maps} (errores {result.MapErrors}, warnings {result.MapWarnings}, info {result.MapInfos}){Environment.NewLine}" +
                $"Mundos: {result.Worlds} (errores {result.WorldErrors}, warnings {result.WorldWarnings}, info {result.WorldInfos}){Environment.NewLine}" +
                $"Indices: {result.IndexFiles} archivos (errores {result.IndexErrors}, warnings {result.IndexWarnings}, info {result.IndexInfos}){Environment.NewLine}{Environment.NewLine}" +
                $"Reportes en:{Environment.NewLine}{result.Directory}",
                "Auditoría completa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            TryOpenFolder(result.Directory);
        }
        catch (Exception error)
        {
            loading.Close();
            MessageBox.Show(this, error.Message, "Error en auditoría", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { loading.Close(); }
    }

    private (string Csm, string Map, string Dat, string Inf) ResolveGlobalOutputPaths(string csmPath)
    {
        var fullCsm = Path.GetFullPath(csmPath);
        if (Path.GetFileName(fullCsm).Equals("world.csm", StringComparison.OrdinalIgnoreCase))
        {
            var outputRoot = ClientResourceRoot;
            var datDirectory = Path.Combine(outputRoot, "Dat");
            var outputWorld = Path.Combine(outputRoot, "world");
            Directory.CreateDirectory(outputWorld);
            Directory.CreateDirectory(datDirectory);
            // Keep the selected WORLD archive in the client/server resource tree,
            // even when the source maps were read from reference/Recursos.
            var outputCsm = Path.Combine(outputWorld, "world.csm");
            return (outputCsm, Path.Combine(outputRoot, "maps", "world.map"),
                Path.Combine(datDirectory, "world.dat"), Path.Combine(datDirectory, "world.inf"));
        }
        return (fullCsm, Path.ChangeExtension(fullCsm, ".map"), Path.ChangeExtension(fullCsm, ".dat"), Path.ChangeExtension(fullCsm, ".inf"));
    }

    private static string ClientResourceRoot => ResourceRootResolver.GeneratedRoot();

    private string SourceMapsDirectory =>
        Directory.Exists(Path.Combine(ResourceRootResolver.ReferenceRoot(), "Mapas"))
            ? Path.Combine(ResourceRootResolver.ReferenceRoot(), "Mapas")
            : Path.Combine(_session.ResourceRoot, "Mapas");

    private void RunGlobalArchiveAudit(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "WORLD (*.csm)|*.csm|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar world.csm"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var output = ResolveGlobalOutputPaths(dialog.FileName);
            var result = new GlobalArchiveAuditService().Audit(dialog.FileName, _session.CurrentWorld, output.Dat, output.Inf, output.Map);
            var semantic = _session.CurrentWorld is null
                ? new GlobalSemanticAuditResult([])
                : new GlobalWorldSemanticAuditService().Audit(_session.CurrentWorld, _session.Resources, dialog.FileName, output.Inf);
            var errors = result.Issues.Where(issue => issue.Error).ToArray();
            var warnings = result.Issues.Where(issue => !issue.Error).ToArray();
            _status.Text = $"Auditoría global: {result.Maps} mapas, {errors.Length + semantic.Errors} errores, {warnings.Length + semantic.Warnings} advertencias";
            var structuralDetails = result.Issues.Count == 0
                ? "No se encontraron inconsistencias."
                : string.Join(Environment.NewLine, result.Issues.Select(issue =>
                    $"{(issue.Error ? "ERROR" : "WARN")} [{issue.Code}] {issue.Message}"));
            var semanticDetails = semantic.Issues.Count == 0
                ? "No se encontraron inconsistencias semánticas en el ensamblado."
                : string.Join(Environment.NewLine, semantic.Issues.Select(issue =>
                    $"{issue.Severity.ToString().ToUpperInvariant()} [{issue.Code}] mapa={issue.MapId} local={issue.LocalX},{issue.LocalY} global={issue.GlobalX},{issue.GlobalY}: {issue.Message}"));
            MessageBox.Show(this,
                $"Auditoría de WORLD terminada.{Environment.NewLine}{Environment.NewLine}" +
                "[Estructura binaria]" + Environment.NewLine + structuralDetails + Environment.NewLine + Environment.NewLine +
                "[Coherencia semántica]" + Environment.NewLine + semanticDetails,
                "Auditoría global", MessageBoxButtons.OK,
                errors.Length + semantic.Errors == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error en auditoría global", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RunGlobalBlockingAudit(object? sender, EventArgs e)
    {
        var world = _session.CurrentWorld;
        if (world is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Auditoría de bloqueos");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Informe TSV (*.tsv)|*.tsv|Todos los archivos|*.*",
            FileName = "auditoria-bloqueos.tsv",
            Title = "Guardar auditoría global de bloqueos"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var service = new GlobalBlockingAuditService();
            var report = service.Audit(world, _session.Resources.Objects);
            service.WriteReport(report, dialog.FileName);
            _status.Text = $"Auditoría de bloqueos: {report.MapsVisited} mapas, {report.Issues.Count} incidencias";
            MessageBox.Show(this,
                $"Auditoría terminada.{Environment.NewLine}{Environment.NewLine}" +
                $"Mapas: {report.MapsVisited}{Environment.NewLine}" +
                $"Celdas: {report.CellsVisited}{Environment.NewLine}" +
                $"Incidencias: {report.Issues.Count}{Environment.NewLine}{Environment.NewLine}" +
                $"Informe: {dialog.FileName}",
                "Auditoría de bloqueos", MessageBoxButtons.OK,
                report.Issues.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error en auditoría de bloqueos", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CorrectGlobalBlockingCopy(object? sender, EventArgs e)
    {
        var world = _session.CurrentWorld;
        if (world is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Corregir bloqueos");
            return;
        }

        if (MessageBox.Show(this,
                "Esta operación copiará todos los CSM a otra carpeta y aplicará allí los bloqueos configurados. " +
                "Nunca modifica los archivos originales. ¿Continuar?",
                "Corregir bloqueos en copia", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        using var dialog = new FolderBrowserDialog { Description = "Carpeta destino para la copia corregida de los CSM" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            new GlobalBlockingAuditService().ApplyCorrectionsToCopy(world, dialog.SelectedPath, _session.Resources.Objects);
            _status.Text = $"Bloqueos corregidos en copia: {dialog.SelectedPath}";
            MessageBox.Show(this, "La copia corregida fue generada. Los CSM originales no fueron modificados.",
                "Corregir bloqueos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al corregir bloqueos", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void GenerateWorldOutputs(object? sender, EventArgs e)
    {
        if (MessageBox.Show(this,
                "Genera NameMapa.dat, translados.ini y minimapas en artifacts/world. " +
                "Los recursos quedan intactos. Continuar?",
                "Salidas auxiliares", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        using var loading = CreateProgressForm("Generando salidas auxiliares");
        var label = (Label)loading.Controls[0];
        var bar = (ProgressBar)loading.Controls[1];
        var progress = new Progress<WorldOutputProgress>(update =>
        {
            label.Text = $"{update.File} ({update.Current}/{update.Total})";
            bar.Value = Math.Clamp((int)Math.Round(update.Current * 100d / Math.Max(1, update.Total)), 0, 100);
        });
        loading.Show(this);
        try
        {
            var maps = Path.Combine(_session.ResourceRoot, "Mapas");
            var result = await Task.Run(() => new WorldOutputService().Generate(
                maps, _session.Resources, null, true, progress, _session.ResourceRoot));
            var outputIssues = new WorldOutputAuditService().Audit(maps, result.Directory, true, _session.ResourceRoot);
            var outputErrors = outputIssues.Count(issue => issue.Severity == AuditSeverity.Error);
            var outputWarnings = outputIssues.Count(issue => issue.Severity == AuditSeverity.Warning);
            var report = new WorldOutputAuditService().WriteReport(outputIssues, result.Directory);
            loading.Close();
            _status.Text = $"Salidas: {result.Maps} mapas, {result.Minimaps} PNG, {result.ClientMinimaps} BMP | auditoria E={outputErrors} W={outputWarnings}";
            MessageBox.Show(this,
                $"Salidas generadas.{Environment.NewLine}{Environment.NewLine}" +
                $"Mapas: {result.Maps}{Environment.NewLine}" +
                $"Minimapas PNG: {result.Minimaps}{Environment.NewLine}" +
                $"Minimapas BMP (cliente): {result.ClientMinimaps}{Environment.NewLine}" +
                $"Errores de generacion: {result.Errors}{Environment.NewLine}" +
                $"Auditoria de salidas: errores={outputErrors}, warnings={outputWarnings}{Environment.NewLine}" +
                $"Reporte: {Path.GetFileName(report)}{Environment.NewLine}{Environment.NewLine}" +
                result.Directory,
                "Salidas auxiliares", MessageBoxButtons.OK, MessageBoxIcon.Information);
            TryOpenFolder(result.Directory);
        }
        catch (Exception error)
        {
            loading.Close();
            MessageBox.Show(this, error.Message, "Error al generar salidas", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { loading.Close(); }
    }

    private Form CreateProgressForm(string title)
    {
        var loading = new Form
        {
            Text = title,
            Width = 560,
            Height = 110,
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ControlBox = false
        };
        var label = new Label { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 0), Text = "Preparando..." };
        var bar = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };
        loading.Controls.Add(label);
        loading.Controls.Add(bar);
        loading.Location = new Point(
            Location.X + Math.Max(0, (Width - loading.Width) / 2),
            Location.Y + Math.Max(0, (Height - loading.Height) / 2));
        return loading;
    }

    private static void TryOpenFolder(string directory)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", directory)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async void PreloadWorldInteractive(object? sender, EventArgs e)
    {
        var world = _session.CurrentWorld;
        if (world is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Precargar mundo");
            return;
        }
        if (MessageBox.Show(this,
                "Esto carga todos los mapas del mundo en memoria (varios GB en WORLDMAP1). Continuar?",
                "Precargar mundo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        UseWaitCursor = true;
        _status.Text = "Precargando mundo...";
        try
        {
            await Task.Run(() => _session.PreloadWorld());
            _status.Text = $"Mundo precargado: {world.LoadedMapCount} mapas";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error al precargar mundo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private void ValidateWorldLayout(object? sender, EventArgs e)
    {
        if (_session.CurrentWorld is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Validar mundo");
            return;
        }
        var official = _session.CurrentWorld.Assembly;
        var issues = new GlobalWorldLayoutValidator().Validate(official, _session.CurrentWorld.Maps, _session.CurrentWorld.RootMap);
        _status.Text = issues.Count == 0 ? "Layout oficial validado sin diferencias" : $"Layout oficial: {issues.Count} diferencias";
        if (issues.Count > 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, issues.Take(30).Select(issue => $"Mapa {issue.MapId}: {issue.Message}")),
                "Diferencias del layout oficial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ExportWorldLayoutAudit(object? sender, EventArgs e)
    {
        if (_session.CurrentWorld is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Auditoría visual");
            return;
        }
        using var dialog = new FolderBrowserDialog { Description = "Carpeta para las auditorías PNG de cada WORLDMAP" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var result = new WorldLayoutAuditService().Export(_session.CurrentWorld.WorldLayouts,
                _session.CurrentWorld.Maps, _session.Resources.Graphics, dialog.SelectedPath);
            _status.Text = $"{result} (proyeccion oficial 100x100; la vista de edicion usa 75x81)";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Error en auditoría visual", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSpecialWorlds(object? sender, EventArgs e)
    {
        if (_session.CurrentWorld is null)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Mundos especiales");
            return;
        }
        using var dialog = new Form { Text = "Mundos especiales", Width = 620, Height = 360, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill };
        var portals = _session.CurrentWorld.SpecialWorlds.SelectMany(pair =>
            pair.Value.Portals.Select(portal => (Name: pair.Key, Portal: portal))).ToArray();
        foreach (var item in portals)
            list.Items.Add($"[{item.Name}] {item.Portal.SourceMap}:{item.Portal.SourceX},{item.Portal.SourceY} -> " +
                $"{item.Portal.DestinationMap}:{item.Portal.DestinationX},{item.Portal.DestinationY}");
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var portal = portals[list.SelectedIndex].Portal;
            if (_session.CurrentWorld.TryGetGlobalTile(portal.DestinationMap, portal.DestinationX, portal.DestinationY, out var global))
            {
                _renderControl.CenterOnGlobalTile(global.X, global.Y);
                dialog.Close();
                _status.Text = $"Destino centrado: mapa {portal.DestinationMap} {portal.DestinationX},{portal.DestinationY}";
            }
            else if (_session.CurrentWorld.Paths.TryGetValue(portal.DestinationMap, out var specialPath) &&
                MessageBox.Show(dialog,
                    (_session.CurrentWorld.Assembly.Placements.Any(placement => placement.MapId == portal.DestinationMap)
                        ? "El tile destino es de borde y no se ve en la vista global."
                        : "El destino no está en el layout activo.") +
                    " ¿Abrirlo como mapa individual?",
                    "Navegación", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _session.LoadMap(specialPath);
                _miniMap.SetWorld(null, null, () => _renderControl.GlobalViewport, _renderControl.CenterOnGlobalTile);
                _renderControl.SetMap(_session.CurrentMap!, _session.Resources);
                dialog.Close();
                _status.Text = $"Mapa especial abierto: {portal.DestinationMap}";
            }
            else MessageBox.Show(dialog, "No existe el archivo del mapa destino.", "Navegación");
        };
        dialog.Controls.Add(list);
        dialog.ShowDialog(this);
    }

    /// <summary>
    /// Diagnostic startup path (AO_WORLD=n): opens the world without dialogs so
    /// performance and captures can be measured from a script.
    /// </summary>
    private async void OpenWorldAt(int worldId)
    {
        var worldDirectory = Path.Combine(_session.ResourceRoot, "world");
        var directory = File.Exists(Path.Combine(worldDirectory, "world.csm"))
            ? worldDirectory
            : SourceMapsDirectory;
        var world = await Task.Run(() => _session.LoadWorld(directory));
        if (worldId > 0 && _session.CurrentWorld is { } current &&
            current.WorldLayouts.Any(layout => layout.WorldId == worldId))
            _session.SwitchWorldLevel(worldId);
        _renderControl.SetGlobalWorld(world, _session.Resources);
        _miniMap.SetWorld(world, _session.Resources.Graphics, () => _renderControl.GlobalViewport,
            _renderControl.CenterOnGlobalTile, _session.ResourceRoot);
        // AO_GOTO was a diagnostic hook; it is disabled because centering the
        // global view while the world warm-up is in flight froze the UI.
        UpdateTitle();
    }

    private static void LogStartupError(Exception error)
    {
        try
        {
            File.AppendAllText(EditorPaths.LogFile("startup-error.log"),
                $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>
    /// Radar: crop of the world image (per-map minimaps composed) centered on the
    /// viewport center, like the VB6 client's RenderMinimapCentered. In map mode it
    /// shows the whole map minimap, like the client's LoadMinimap mode.
    /// </summary>
    private void UpdateRadar()
    {
        if (!_radarEnabled) return;
        if (_compiledUnifiedWorld is { } unified)
        {
            var (x, y) = _renderControl.ViewportCenterGlobalTile;
            if (!_radarDirty && x == _radarX && y == _radarY) return;
            _radarX = x; _radarY = y; _radarDirty = false;
            try
            {
                var radar = new UnifiedWorldRadarService();
                var crop = radar.CreateCentered(unified,
                    Math.Clamp(x, 0, unified.Width - 1),
                    Math.Clamp(y, 0, unified.Height - 1),
                    WorldRadarService.DefaultSize, WorldRadarService.DefaultSize);
                _radarView.SetImage(crop.Image, crop.MarkerX, crop.MarkerY);
            }
            catch (Exception error) { LogRadarError(error); }
            return;
        }
        var service = new WorldRadarService();
        if (_session.CurrentWorld is { } world && _miniMap.RadarImage is { } worldImage)
        {
            var (x, y) = _renderControl.ViewportCenterGlobalTile;
            if (!_radarDirty && x == _radarX && y == _radarY) return;
            _radarX = x; _radarY = y; _radarDirty = false;
            try
            {
                if (!world.TryResolve(x, y, out var location))
                {
                    // The camera can sit on a map that failed to load or on the
                    // blank band between maps. Do not keep the previous crop: it
                    // would show a place that is not the one on screen.
                    _radarView.SetImage(null);
                    if (Environment.GetEnvironmentVariable("AO_RADAR_DEBUG") == "1")
                        LogRadarDebug($"g=({x},{y}) sin mapa resoluble: radar limpio", null);
                    return;
                }
                var chunk = new GlobalChunkCoordinate(
                    (int)MathF.Floor(x / (float)GlobalChunkGeometry.WidthTiles),
                    (int)MathF.Floor(y / (float)GlobalChunkGeometry.HeightTiles));
                // The radar image uses the collapsed grid (see WorldBorderStitch), so
                // ask the sidebar for the pixel: it owns the composition.
                var pixel = _miniMap.RadarPixel(chunk, location.LocalX, location.LocalY) ??
                    WorldRadarService.ToRadarPixel(chunk, location.LocalX, location.LocalY);
                var (px, py) = pixel;
                var stitch = _miniMap.DebugStitch(chunk);
                var (crop, markerX, markerY) = service.CreateCenteredAt(worldImage, px, py,
                    WorldRadarService.DefaultSize, WorldRadarService.DefaultSize);
                _radarView.SetImage(crop, markerX, markerY);
                if (Environment.GetEnvironmentVariable("AO_RADAR_DEBUG") == "1")
                    LogRadarDebug(world, x, y, location, chunk, px, py, worldImage, crop, markerX, markerY, stitch);
            }
            catch (Exception error) { LogRadarError(error); }
            return;
        }
        if (_session.CurrentMap is not null && _miniMap.MapImage is { } mapImage)
        {
            if (!_radarDirty) return;
            _radarDirty = false;
            try { _radarView.SetImage(new Bitmap(mapImage)); }
            catch (Exception error) { LogRadarError(error); }
        }
    }

    /// <summary>Click on the radar: recenter the view by the clicked tile offset.</summary>
    private void CenterFromRadar(int dx, int dy)
    {
        if (_session.CurrentWorld is not null)
        {
            var (x, y) = _renderControl.ViewportCenterGlobalTile;
            _renderControl.CenterOnGlobalTile(x + dx, y + dy);
        }
        else if (_session.CurrentMap is not null)
        {
            var (x, y) = _renderControl.ViewportCenterTile;
            _renderControl.CenterOnTile(Math.Clamp(x + dx, 1, WorldMap.Width), Math.Clamp(y + dy, 1, WorldMap.Height));
        }
        _radarDirty = true;
        UpdateRadar();
    }

    private static void LogRadarError(Exception error)
    {
        try
        {
            File.AppendAllText(EditorPaths.LogFile("radar-error.log"),
                $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>
    /// AO_RADAR_DEBUG=1: records whether the pixel under the radar marker is the
    /// same terrain the viewport center shows (minimap colour of that tile).
    /// </summary>
    private void LogRadarDebug(GlobalWorldDocument world, int x, int y, GlobalTileLocation location,
        GlobalChunkCoordinate chunk, int px, int py, Bitmap worldImage, Bitmap crop,
        int markerX, int markerY, (int X, int Y) stitch)
    {
        try
        {
            var marked = BitmapPixel(crop, markerX, markerY);
            var expected = 0;
            var sample = "sin mapa";
            if (world.Maps.TryGetValue(location.MapId, out var map) &&
                location.LocalX >= 1 && location.LocalX <= WorldMap.Width &&
                location.LocalY >= 1 && location.LocalY <= WorldMap.Height)
            {
                var layer1 = map.Blocks[location.LocalX, location.LocalY].Graphics[0].GraphicIndex;
                var graphics = _session.Resources.Graphics;
                if (layer1 > 0 && layer1 < graphics.Count)
                {
                    using var averages = new GraphicAverageColorService(_session.ResourceRoot);
                    expected = averages.Get(graphics[layer1]);
                    if (expected == 0 && graphics[layer1].MiniMapColor != 0)
                        expected = MapMiniMapService.FromColorRef(graphics[layer1].MiniMapColor).ToArgb();
                }
                var data = layer1 > 0 && layer1 < graphics.Count ? graphics[layer1] : default(GraphicData);
                sample = $"layer1={layer1} minimap={Color.FromArgb(expected)} " +
                    $"grh=(file={data?.FileNumber} px={data?.PixelWidth}x{data?.PixelHeight} " +
                    $"src={data?.SourceX},{data?.SourceY} frames={data?.Frames.Length} " +
                    $"active={data?.Active} palette={data?.MiniMapColor:X8})";
            }
            var aligned = marked != Color.Black.ToArgb() && marked == expected;
            LogRadarDebugLine($"g=({x},{y}) map={location.MapId} local=({location.LocalX},{location.LocalY}) " +
                $"chunk=({chunk.X},{chunk.Y}) px=({px},{py}) stitch=({stitch.X},{stitch.Y}) " +
                $"imagen={worldImage.Width}x{worldImage.Height} " +
                $"marca=({markerX},{markerY}) pixel={Color.FromArgb(marked)} {sample} " +
                $"alineado={(aligned ? "si" : marked == Color.Black.ToArgb() ? "sin-construir" : "NO")}");
            DumpRadarDebug(world, location, worldImage);
        }
        catch { }
    }

    /// <summary>AO_RADAR_DEBUG=1: a state line that has no tile (unresolved center).</summary>
    private static void LogRadarDebug(string message, Bitmap? _) => LogRadarDebugLine(message);

    private static void LogRadarDebugLine(string message)
    {
        try
        {
            File.AppendAllText(EditorPaths.LogFile("radar-debug.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static int BitmapPixel(Bitmap image, int x, int y) =>
        x >= 0 && y >= 0 && x < image.Width && y < image.Height ? image.GetPixel(x, y).ToArgb() : 0;

    private static int _radarDebugDumpCount;

    /// <summary>AO_RADAR_DEBUG=1: writes the radar image and the map minimap.</summary>
    private void DumpRadarDebug(GlobalWorldDocument world, GlobalTileLocation location, Bitmap worldImage)
    {
        // First crop plus one snapshot every ~9 s (the timer ticks every 150 ms).
        if (++_radarDebugDumpCount % 60 != 1) return;
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "evidence", "radar-debug");
            Directory.CreateDirectory(folder);
            var worldCopy = new Bitmap(worldImage);
            worldCopy.Save(Path.Combine(folder, $"world-radar-{_radarDebugDumpCount}.png"),
                System.Drawing.Imaging.ImageFormat.Png);
            worldCopy.Dispose();
            var placements = string.Join(Environment.NewLine, world.Assembly.Placements
                .OrderBy(item => item.Chunk.Y).ThenBy(item => item.Chunk.X)
                .Select(item => $"{item.Chunk.X},{item.Chunk.Y} -> {item.MapId} ({item.LevelId})"));
            File.WriteAllText(Path.Combine(folder, "placements.txt"),
                $"width={world.Assembly.Width} height={world.Assembly.Height}" +
                $"{Environment.NewLine}{placements}");
            if (!world.Maps.TryGetValue(location.MapId, out var map)) return;
            _miniMap.DebugMapImage(location.MapId)?.Save(Path.Combine(folder, $"built{location.MapId}.png"),
                System.Drawing.Imaging.ImageFormat.Png);
            using var averages = new GraphicAverageColorService(_session.ResourceRoot);
            using var minimap = new MapMiniMapService().CreateSmooth(map, _session.Resources.Graphics,
                averages, new MapMiniMapOptions(ShowLayer2: false, ShowLayer3: false, ShowLayer4: false));
            minimap.Save(Path.Combine(folder, $"map{location.MapId}.png"),
                System.Drawing.Imaging.ImageFormat.Png);
        }
        catch { }
    }

    private void SwitchWorldLevel(object? sender, EventArgs e)
    {
        if (_session.CurrentWorld is null || _session.CurrentWorld.WorldLayouts.Count == 0)
        {
            MessageBox.Show(this, "Primero abra un mundo completo.", "Cambiar mundo");
            return;
        }
        using var dialog = new Form { Text = "Seleccionar mundo/nivel", Width = 280, Height = 130, StartPosition = FormStartPosition.CenterParent };
        var worlds = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        worlds.Items.AddRange(_session.CurrentWorld.WorldLayouts.Select(layout => (object)$"WORLDMAP{layout.WorldId} ({layout.Width}x{layout.Height})").ToArray());
        worlds.SelectedIndex = 0;
        var accept = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(accept); dialog.Controls.Add(worlds); dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK || worlds.SelectedIndex < 0) return;
        var worldId = _session.CurrentWorld.WorldLayouts[worlds.SelectedIndex].WorldId;
        if (_session.SwitchWorldLevel(worldId))
        {
            _renderControl.SetGlobalWorld(_session.CurrentWorld, _session.Resources);
            _miniMap.SetWorld(_session.CurrentWorld, _session.Resources.Graphics, () => _renderControl.GlobalViewport,
                _renderControl.CenterOnGlobalTile, _session.ResourceRoot);
            _status.Text = $"Mundo/nivel WORLDMAP{worldId} activo";
        }
    }
}
