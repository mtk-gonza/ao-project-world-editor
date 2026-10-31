using AoProject.WorldEditor.Domain;
using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Infrastructure.Runtime;

namespace AoProject.WorldEditor.Application;

/// <summary>Coordinates resource loading and the current editable map.</summary>
internal sealed class WorldEditorSession
{
    private readonly SectorCsmFileService _csmMaps = new();
    private readonly MapEditingService _editing = new();
    private readonly MapBulkEditingService _bulk = new();
    private readonly MapCleanupService _cleanup = new();
    private readonly SurfaceEditingService _surfaces = new();
    private readonly MapOptimizationService _optimization = new();
    private readonly MapInfoEditingService _mapInfo = new();
    private readonly AdjacentTransferService _adjacentTransfers = new();
    private readonly ResourceIndexRefreshService _indexRefresh = new();
    private readonly MapReplacementService _replacement = new();
    private readonly TreePlacementService _trees = new();
    private readonly SurfacePlacementService _surfacePlacement = new();
    private readonly MapEntityEditingService _entities = new();
    private readonly MapEnvironmentEditingService _environment = new();
    private readonly TransferShiftService _transferShift = new();
    private readonly AmbientationPlacementService _ambientiationPlacement = new();
    private readonly MapEffectsEditingService _effects = new();
    private readonly RandomEntityPlacementService _randomEntities = new();
    private readonly RandomSurfacePlacementService _randomSurface = new();
    private readonly SurfaceBorderPlacementService _surfaceBorders = new();
    private readonly SurfaceWholeMapService _surfaceWholeMap = new();
    private const int MaxMapsPerUndoSnapshot = 64;
    private readonly LinkedList<WorldSectorSnapshot> _bulkUndo = [];
    private readonly LinkedList<GlobalWorldSnapshot> _globalUndo = [];
    private WorldEditorResourceCatalog? _resources;
    private readonly object _resourceSync = new();

    public WorldEditorSession(string resourceRoot)
    {
        ResourceRoot = Path.GetFullPath(resourceRoot);
    }

    public string ResourceRoot { get; }
    /// <summary>Immutable source tree used for legacy map and world metadata import.</summary>
    public string ReferenceResourceRoot => ResourceRootResolver.ReferenceRoot();
    public bool UseUndo { get; private set; } = true;
    /// <summary>True when the last global operation was too large to snapshot.</summary>
    public bool UndoOmitted { get; private set; }
    public WorldSector? CurrentMap { get; private set; }
    public string? CurrentMapPath { get; private set; }
    public GlobalWorldDocument? CurrentWorld { get; private set; }

    /// <summary>
    /// Compiles the currently loaded legacy assembly into one rectangular map.
    /// The source CSM files remain untouched.
    /// </summary>
    public UnifiedWorldCompileResult CompileUnifiedWorld()
    {
        if (CurrentWorld is null)
            throw new InvalidOperationException("No hay un mundo global cargado.");
        return new UnifiedWorldCompiler().Compile(CurrentWorld, Resources);
    }

    /// <summary>Saves the compiled single-map artifact without modifying CSMs.</summary>
    public UnifiedWorldCompileResult SaveUnifiedWorld(string path)
    {
        var result = CompileUnifiedWorld();
        new UnifiedWorldFileService().Save(result.Map, path);
        return result;
    }

    public UnifiedWorldMap LoadUnifiedWorld(string path) => new UnifiedWorldFileService().Load(path);

    public UnifiedWorldBundle CompileUnifiedWorldBundle(bool applyStitching = false)
    {
        if (CurrentWorld is null) throw new InvalidOperationException("No hay un mundo global cargado.");
        var assignments = new UnifiedWorldFloorAssignmentService()
            .Detect(CurrentWorld);
        var settlements = Path.Combine(ResourceRoot, "world", "settlements.json");
        var dat = Path.Combine(ResourceRoot, "Dat");
        return new UnifiedWorldCompiler().CompileFloors(CurrentWorld, Resources, assignments, applyStitching,
            File.Exists(settlements) ? File.ReadAllBytes(settlements) : [],
            File.Exists(Path.Combine(dat, "SectorId.dat")) ? File.ReadAllBytes(Path.Combine(dat, "SectorId.dat")) : [],
            File.Exists(Path.Combine(dat, "UnderworldMapPool.dat")) ? File.ReadAllBytes(Path.Combine(dat, "UnderworldMapPool.dat")) : [],
            File.Exists(Path.Combine(dat, "PhoenixMapPool.dat")) ? File.ReadAllBytes(Path.Combine(dat, "PhoenixMapPool.dat")) : []);
    }

    public UnifiedWorldBundle CompileStitchedWorldBundle() => CompileUnifiedWorldBundle(true);

    public UnifiedWorldBundle SaveUnifiedWorldBundle(string directory, bool applyStitching = false)
    {
        var bundle = CompileUnifiedWorldBundle(applyStitching);
        new UnifiedWorldBundleFileService().Save(bundle, directory);
        var mapsDirectory = Path.Combine(Directory.GetParent(Path.GetFullPath(directory))?.FullName ?? directory, "maps");
        new UnifiedWorldImageService().Save(bundle, Resources, mapsDirectory);
        SaveStitchAudits(directory);
        return bundle;
    }

    public void SaveStitchAudits(string directory)
    {
        if (CurrentWorld is null) throw new InvalidOperationException("No hay un mundo global cargado.");
        Directory.CreateDirectory(directory);
        var audit = new GlobalStitchAuditService();
        audit.SaveTsv(Path.Combine(directory, "stitch-audit.tsv"), audit.Audit(CurrentWorld));
        audit.SaveCompactedMaterializationTsv(
            Path.Combine(directory, "stitch-compile-audit.tsv"),
            audit.AuditCompactedMaterialization(CurrentWorld));
        audit.SaveConflictTsv(
            Path.Combine(directory, "stitch-conflict-audit.tsv"),
            audit.AuditCompactedConflicts(CurrentWorld));
        audit.SaveExtendedExitTsv(
            Path.Combine(directory, "extended-exit-audit.tsv"),
            audit.AuditExtendedExits(CurrentWorld));
        audit.SaveMapLevelsTsv(
            Path.Combine(directory, "map-level-audit.tsv"),
            audit.AuditMapLevels(CurrentWorld));
        var looseMaps = Path.Combine(ReferenceResourceRoot, "Mapas");
        if (Directory.Exists(looseMaps))
            new LooseMapTeleportAuditService().SaveTsv(
                Path.Combine(directory, "loose-map-teleport-audit.tsv"),
                new LooseMapTeleportAuditService().Audit(looseMaps));
    }

    public UnifiedWorldBundle LoadUnifiedWorldBundle(string directory) =>
        new UnifiedWorldBundleFileService().Load(directory);

    public UnifiedWorldMap LoadUnifiedWorldFloor(string directory, int floorZ) =>
        new UnifiedWorldFileService().Load(Path.Combine(directory, $"floor-{floorZ:+#;-#;0}.gmap"));

    public bool ActivateWorldMap(short mapId)
    {
        if (CurrentWorld is null || !CurrentWorld.Maps.TryGetValue(mapId, out var map)) return false;
        if (!CurrentWorld.Paths.TryGetValue(mapId, out var path)) return false;
        if (ReferenceEquals(CurrentMap, map))
        {
            CurrentWorld.SetActiveMap(mapId);
            return true;
        }
        CurrentWorld.SetActiveMap(mapId);
        CurrentMap = map;
        CurrentMapPath = path;
        // Local undo entries are full snapshots of CurrentMap without a map id;
        // they cannot be replayed on a different map. Global snapshots are
        // per-map now, so they survive the switch.
        _editing.ClearHistory();
        _bulkUndo.Clear();
        return true;
    }

    public WorldSaveResult SaveWorld(string? backupRoot = null)
    {
        if (CurrentWorld is null) throw new InvalidOperationException("No hay un mundo cargado.");
        var saved = new List<string>();
        var errors = new List<string>();
        var backupDirectory = Path.Combine(
            backupRoot ?? EditorPaths.BackupsDirectory,
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        foreach (var (mapId, map) in CurrentWorld.LoadedMaps)
        {
            if (!map.Info.Changed) continue;
            if (!CurrentWorld.Paths.TryGetValue(mapId, out var path))
            {
                errors.Add($"{mapId}: no tiene ruta asociada.");
                continue;
            }
            try
            {
                if (File.Exists(path))
                {
                    Directory.CreateDirectory(backupDirectory);
                    File.Copy(path, Path.Combine(backupDirectory, Path.GetFileName(path)), overwrite: true);
                }
                _csmMaps.Save(map, path);
                map.Info.Changed = false;
                saved.Add(path);
            }
            catch (Exception error)
            {
                errors.Add($"{Path.GetFileName(path)}: {error.Message}");
            }
        }
        return new WorldSaveResult(saved, errors);
    }

    public int ClearGlobalSelectionGraphics(GlobalTileSelection selection) =>
        ApplyGlobalSelection(selection, block =>
        {
            for (var layer = 0; layer < 4; layer++) block.Graphics[layer].GraphicIndex = 0;
        });

    public int ToggleGlobalSelectionBlocked(GlobalTileSelection selection) =>
        ApplyGlobalSelection(selection, block => block.Blocked = block.Blocked > 0 ? (byte)0 : (byte)0xF);

    public int ApplyGlobalBlockMask(GlobalTileSelection selection, byte mask) =>
        ApplyGlobalSelection(selection, block => block.Blocked = mask);

    public void CopyGlobal(MapClipboardService clipboard, GlobalTileSelection selection) =>
        clipboard.CopyGlobal(CurrentWorldRequired(), selection);

    public int CutGlobal(MapClipboardService clipboard, GlobalTileSelection selection)
    {
        var world = CurrentWorldRequired();
        CaptureGlobalUndo(world.CollectSectorIds(selection));
        return clipboard.CutGlobal(world, selection);
    }

    public int PasteGlobal(MapClipboardService clipboard, int globalX, int globalY)
    {
        var world = CurrentWorldRequired();
        if (clipboard.ClipboardSize is not { } size)
            throw new InvalidOperationException("No hay un bloque copiado.");
        CaptureGlobalUndo(world.CollectSectorIds(globalX, globalY,
            globalX + size.Width - 1, globalY + size.Height - 1));
        return clipboard.PasteGlobal(world, globalX, globalY);
    }

    private int ApplyGlobalSelection(GlobalTileSelection selection, Action<SectorTile> operation)
    {
        if (CurrentWorld is null) throw new InvalidOperationException("No hay un mundo cargado.");
        CaptureGlobalUndo(CurrentWorld.CollectSectorIds(selection));
        var normalized = selection.Normalize();
        var changed = 0;
        for (var y = normalized.StartY; y <= normalized.EndY; y++)
        for (var x = normalized.StartX; x <= normalized.EndX; x++)
        {
            if (!CurrentWorld.TryResolve(x, y, out var location)) continue;
            if (!CurrentWorld.Maps.TryGetValue(location.SectorId, out var map)) continue;
            var block = map.Blocks[location.LocalX, location.LocalY];
            operation(block);
            map.Info.Changed = true;
            changed++;
        }
        return changed;
    }

    private void CaptureGlobalUndo(IEnumerable<short> mapIds)
    {
        UndoOmitted = false;
        if (!UseUndo || CurrentWorld is null) return;
        var ids = mapIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        // A single full-world selection would still snapshot ~1.5 GB. Skip
        // undo for those huge operations instead of risking OOM; the UI warns.
        if (ids.Length > MaxMapsPerUndoSnapshot)
        {
            UndoOmitted = true;
            return;
        }
        _globalUndo.AddFirst(GlobalWorldSnapshot.Capture(CurrentWorld, ids));
        while (_globalUndo.Count > 30) _globalUndo.RemoveLast();
        // Bound by map snapshots (~3.6 MB each) instead of entry count.
        var totalMaps = _globalUndo.Sum(snapshot => snapshot.MapCount);
        while (_globalUndo.Count > 1 && totalMaps > 200)
        {
            totalMaps -= _globalUndo.Last!.Value.MapCount;
            _globalUndo.RemoveLast();
        }
    }

    private GlobalWorldDocument CurrentWorldRequired() =>
        CurrentWorld ?? throw new InvalidOperationException("No hay un mundo cargado.");
    public string? AutoSave(int minutes, string destinationRoot)
    {
        var service = new AutoSaveService();
        service.Configure(minutes);
        return service.Save(CurrentMapRequired(), CurrentMapPath, destinationRoot);
    }
    public WorldEditorResourceCatalog Resources
    {
        get
        {
            lock (_resourceSync)
                return _resources ??= WorldEditorResourceCatalog.Load(ResourceRoot);
        }
    }
    public ResourceIndexRefreshResult RefreshResourceIndices()
    {
        lock (_resourceSync)
        {
            (_resources, var result) = _indexRefresh.Reload(ResourceRoot);
            return result;
        }
    }
    public int ReplaceGraphic(int fromLayer, int toLayer, int fromGraphic, int toGraphic) =>
        ExecuteBulk(map => _replacement.ReplaceGraphic(map, fromLayer, toLayer, fromGraphic, toGraphic));
    public int ReplaceObject(short from, short to) => ExecuteBulk(map => _replacement.ReplaceObject(map, Resources.Objects, from, to));
    public int ReplaceNpc(short from, short to) => ExecuteBulk(map => _replacement.ReplaceNpc(map, Resources.Npcs, from, to));
    public int ReplaceTrigger(short from, short to) => ExecuteBulk(map => _replacement.ReplaceTrigger(map, from, to));
    public int PlaceTrees(TreePlacementOptions options) => ExecuteBulk(map => _trees.Place(map, Resources.Objects, options));
    public int BlockConfiguredObjects() => ExecuteBulk(map => _trees.BlockConfiguredObjects(map, Resources.Objects));
    public int PlaceSurface(SurfaceData surface, int x, int y, bool mosaic = false) =>
        ExecuteBulk(map => _surfacePlacement.Place(map, surface, x, y, mosaic));
    public void PaintSurfaceAt(int x, int y, int layer, int graphic, int width, int height, bool blocked) =>
        ExecuteBulk(map => _surfacePlacement.PaintAt(map, x, y, layer, graphic, width, height, blocked));
    public int AutoCompleteSurfaceAt(int x, int y, int layer, int graphic, int width, int height, bool blocked) =>
        ExecuteBulk(map => _surfacePlacement.AutoCompleteAt(map, x, y, layer, graphic, width, height, blocked));
    public void ApplyInteractiveClick(int x, int y, int layer, int graphic, bool surfaceSelected,
        bool autoComplete, int width, int height, bool blocked, bool insertBlocking, bool removeBlocking) =>
        ExecuteBulk(map =>
        {
            ValidateTile(map, x, y);
            if (surfaceSelected)
            {
                if (autoComplete)
                    _surfacePlacement.AutoCompleteAt(map, x, y, layer, graphic, width, height, blocked);
                else
                    _surfacePlacement.PaintAt(map, x, y, layer, graphic, width, height, blocked);
            }
            else
            {
                if (layer is < 0 or >= 4 || graphic < 0) throw new ArgumentOutOfRangeException(nameof(layer));
                map.Blocks[x, y].Graphics[layer].GraphicIndex = graphic;
                map.Info.Changed = true;
            }

            if (insertBlocking) map.Blocks[x, y].Blocked = 0xF;
            else if (removeBlocking) map.Blocks[x, y].Blocked = 0;
            map.Info.Changed = true;
        });

    public int FillGraphic(int x, int y, int layer, int graphic)
    {
        return ExecuteBulk(map =>
        {
            ValidateTile(map, x, y);
            if (layer is < 0 or >= 4 || graphic < 0) throw new ArgumentOutOfRangeException(nameof(layer));
            var original = map.Blocks[x, y].Graphics[layer].GraphicIndex;
            if (original == graphic) return 0;

            var pending = new Queue<(int X, int Y)>();
            var visited = new HashSet<(int X, int Y)>();
            pending.Enqueue((x, y));
            var changed = 0;
            while (pending.Count > 0)
            {
                var tile = pending.Dequeue();
                if (!visited.Add(tile) || !InDomain(map, tile.X, tile.Y)) continue;
                if (map.Blocks[tile.X, tile.Y].Graphics[layer].GraphicIndex != original) continue;
                map.Blocks[tile.X, tile.Y].Graphics[layer].GraphicIndex = graphic;
                changed++;
                pending.Enqueue((tile.X - 1, tile.Y)); pending.Enqueue((tile.X + 1, tile.Y));
                pending.Enqueue((tile.X, tile.Y - 1)); pending.Enqueue((tile.X, tile.Y + 1));
            }
            if (changed > 0) map.Info.Changed = true;
            return changed;
        });
    }

    public int ReplaceGraphic(int layer, int fromGraphic, int toGraphic)
    {
        return ExecuteBulk(map =>
        {
            if (layer is < 0 or >= 4 || fromGraphic < 0 || toGraphic < 0)
                throw new ArgumentOutOfRangeException(nameof(layer));
            if (fromGraphic == toGraphic) return 0;
            var changed = 0;
            for (var x = (int)map.CsmSectorSize.XMin; x <= map.CsmSectorSize.XMax; x++)
            for (var y = (int)map.CsmSectorSize.YMin; y <= map.CsmSectorSize.YMax; y++)
            {
                if (!InDomain(map, x, y) || map.Blocks[x, y].Graphics[layer].GraphicIndex != fromGraphic) continue;
                map.Blocks[x, y].Graphics[layer].GraphicIndex = toGraphic;
                changed++;
            }
            if (changed > 0) map.Info.Changed = true;
            return changed;
        });
    }
    public void SetObject(int x, int y, short index, short amount = 1) => ExecuteBulk(map => _entities.SetObject(map, Resources.Objects, x, y, index, amount));
    public void SetNpc(int x, int y, short index) => ExecuteBulk(map => _entities.SetNpc(map, Resources.Npcs, x, y, index));
    public void SetTrigger(int x, int y, short trigger) => ExecuteBulk(map => _entities.SetTrigger(map, x, y, trigger));
    public void SetTransfer(int x, int y, short map, short destinationX, short destinationY) => ExecuteBulk(world => _entities.SetTransfer(world, x, y, map, destinationX, destinationY));
    public void ClearSelectedEntity(int x, int y, bool clearObject, bool clearNpc, bool clearTrigger, bool clearTransfer) => ExecuteBulk(map =>
    {
        ValidateTile(map, x, y);
        var block = map.Blocks[x, y];
        if (clearObject) { block.Object.Index = 0; block.Object.Amount = 0; block.ObjectGraphic = default; }
        if (clearNpc) { block.NpcIndex = 0; block.CharacterIndex = 0; }
        if (clearTrigger) block.Trigger = 0;
        if (clearTransfer) { block.TileExit.SectorId = 0; block.TileExit.X = 0; block.TileExit.Y = 0; }
        map.Info.Changed = true;
    });
    public int ClearMap(int baseGraphic = 1) => ExecuteBulk(map => _cleanup.ClearMap(map, baseGraphic));
    public int RemoveBorders() => ExecuteBulk(map => _cleanup.RemoveBorders(map));
    public void SetAllBlocked(byte value) => ExecuteBulk(map => _bulk.SetBlocked(map, value));
    public void SetTileBlocked(int x, int y, byte value) => ExecuteBulk(map =>
    {
        ValidateTile(map, x, y);
        map.Blocks[x, y].Blocked = value; map.Info.Changed = true;
    });
    public int PlaceRandomSurface(RandomSurfaceOptions options) => ExecuteBulk(map => _randomSurface.Place(map, options));
    public void ApplyEnvironment(byte rain, byte snow, byte fog, int baseLight) => ExecuteBulk(map => _environment.Apply(map, rain, snow, fog, baseLight));
    public int ShiftTransfers(TransferShift shift) => ExecuteBulk(map => _transferShift.Shift(map, shift));
    public int PlaceAmbientation(Ambientation ambientiation, int count, int layer, bool blocked, int seed = 0) =>
        ExecuteBulk(map => _ambientiationPlacement.Place(map, ambientiation, count, layer, blocked, seed));
    public void SetLight(int x, int y, short range, int color) => ExecuteBulk(map => _effects.SetLight(map, x, y, range, color));
    public void SetParticle(int x, int y, int particle) => ExecuteBulk(map => _effects.SetParticle(map, x, y, particle));
    public int ClearLights() => ExecuteBulk(map => _effects.ClearLights(map));
    public int ClearParticles() => ExecuteBulk(map => _effects.ClearParticles(map));
    public void ClearSelectedLight(int x, int y) => ExecuteBulk(map => _effects.SetLight(map, x, y, 0, 0));
    public void ClearSelectedParticle(int x, int y) => ExecuteBulk(map =>
    {
        ValidateTile(map, x, y);
        map.Blocks[x, y].ParticleIndex = 0; map.Blocks[x, y].ParticleGroup = 0; map.Info.Changed = true;
    });
    public int PlaceRandomNpcs(RandomNpcPlacementOptions options) => ExecuteBulk(map => _randomEntities.PlaceNpcs(map, Resources.Npcs, options));
    public int PlaceRandomObjects(RandomObjectPlacementOptions options) => ExecuteBulk(map => _randomEntities.PlaceObjects(map, Resources.Objects, options));
    public (string Path, short X, short Y)? ResolveTransfer(int x, int y)
    {
        var map = CurrentMapRequired();
        ValidateTile(map, x, y);
        var transfer = map.Blocks[x, y].TileExit;
        var path = CurrentMapPath is null ? null : new MapNavigationService().ResolveTransferPath(CurrentMapPath, transfer);
        return path is null ? null : (path, transfer.X, transfer.Y);
    }
    public int PlaceSurfaceOnBorders(int layer, int graphic, bool mosaic, int mosaicWidth, int mosaicHeight, bool blocked) =>
        ExecuteBulk(map => _surfaceBorders.Place(map, layer, graphic, mosaic, mosaicWidth, mosaicHeight, blocked));
    public int PlaceSurfaceOnWholeMap(int layer, int graphic, bool mosaic, int mosaicWidth, int mosaicHeight) =>
        ExecuteBulk(map => _surfaceWholeMap.Place(map, layer, graphic, mosaic, mosaicWidth, mosaicHeight));
    public bool CanUndo => UseUndo && (_globalUndo.Count > 0 || _bulkUndo.Count > 0 || _editing.CanUndo);

    private static bool InDomain(WorldSector map, int x, int y) =>
        x >= map.CsmSectorSize.XMin && x <= map.CsmSectorSize.XMax &&
        y >= map.CsmSectorSize.YMin && y <= map.CsmSectorSize.YMax &&
        x is >= 1 and <= WorldSector.Width && y is >= 1 and <= WorldSector.Height;

    private static void ValidateTile(WorldSector map, int x, int y)
    {
        if (!InDomain(map, x, y)) throw new ArgumentOutOfRangeException(nameof(x));
    }

    public void ConfigureUndo(bool enabled)
    {
        UseUndo = enabled;
        if (!enabled) { _bulkUndo.Clear(); _globalUndo.Clear(); _editing.ClearHistory(); }
    }

    public void LoadMap(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        CurrentMap = Path.GetExtension(path).Equals(".csm", StringComparison.OrdinalIgnoreCase)
            ? _csmMaps.Load(path)
            : new LegacySectorFileService().LoadV3(path);
        CurrentMapPath = Path.GetFullPath(path);
        CurrentWorld = null;
        _editing.ClearHistory();
        _bulkUndo.Clear();
        _globalUndo.Clear();
    }

    public GlobalWorldDocument LoadWorld(string directory, short? rootMap = null, IProgress<WorldLoadProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);

        var files = Directory.EnumerateFiles(directory, "*.csm", SearchOption.AllDirectories)
            .SelectMany(path => IsGlobalWorldFileName(Path.GetFileName(path))
                ? GlobalCsmArchiveService.ReadSectorIds(path).Select(id => (Path: path, Id: id))
                : [ (Path: path, Id: ParseSectorId(path)) ])
            .Where(item => item.Id > 0)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToArray();
        if (files.Length == 0) throw new InvalidOperationException("La carpeta no contiene sectores con archivos CSM.");

        var loadErrors = files.Where(file => new FileInfo(file.Path).Length == 0)
            .Select(file => $"{Path.GetFileName(file.Path)}: archivo vacío").ToList();
        var validFiles = files.Where(file => new FileInfo(file.Path).Length > 0).ToArray();
        if (validFiles.Length == 0)
            throw new InvalidDataException("No se pudo cargar ningún mapa .csm: " + string.Join(" | ", loadErrors));
        var worldData = new MapsWorldDataService();
        var layouts = worldData.Load(SourceResourcePath("init", "mapsworlddata.dat"));
        var availableIds = validFiles.Select(item => item.Id).ToArray();
        var layoutRoot = layouts.Select(layout => MapsWorldDataService.ChooseRoot(layout, availableIds))
            .FirstOrDefault(id => id > 0);
        var selectedRoot = rootMap ?? (layoutRoot > 0 ? layoutRoot : validFiles.Min(item => item.Id));
        if (!availableIds.Contains(selectedRoot)) throw new FileNotFoundException($"No se encontro el mapa raiz {selectedRoot}.");
        var paths = validFiles.ToDictionary(item => item.Id, item => item.Path);
        GlobalWorldAssembly? assembly = MapsWorldDataService.BuildAssembly(layouts, paths.Keys.ToArray(), selectedRoot);
        if (assembly is null)
        {
            // No official layout: retain the old exhaustive path so border
            // exits can still assemble an unlisted/custom world.
            var maps = new Dictionary<short, WorldSector>();
            foreach (var file in validFiles)
            {
                progress?.Report(new WorldLoadProgress(files.Length, maps.Count + loadErrors.Count + 1, Path.GetFileName(file.Path), null));
                try
                {
                    maps[file.Id] = IsGlobalWorldFileName(Path.GetFileName(file.Path))
                        ? _csmMaps.Load(file.Path, file.Id)
                        : _csmMaps.Load(file.Path);
                }
                catch (Exception error) { loadErrors.Add($"{Path.GetFileName(file.Path)}: {error.Message}"); }
            }
            if (!maps.ContainsKey(selectedRoot)) throw new InvalidDataException("No se pudo cargar el mapa raíz.");
            assembly = new GlobalWorldAssembler().Assemble(maps, selectedRoot);
        }
        progress?.Report(new WorldLoadProgress(1, 1, "layout", null));
        CurrentWorld = new GlobalWorldDocument(assembly, paths, selectedRoot,
            (mapId, path) => _csmMaps.Load(path, mapId));
        CurrentWorld.SourcePaths = files.ToDictionary(item => item.Id, item => item.Path);
        CurrentWorld.WorldLayouts = layouts;
        CurrentWorld.FloorAssemblies = layouts
            .Where(layout => layout.WorldId is >= 1 and <= 3)
            .Select(layout => (FloorZ: 1 - layout.WorldId,
                Assembly: MapsWorldDataService.BuildAssembly(layout, paths.Keys.ToArray())))
            .Where(item => item.Assembly is not null)
            .Select(item => (item.FloorZ, item.Assembly!))
            .ToArray();
        CurrentWorld.IgnoredWorldIds = worldData.IgnoredWorldIds;
        var specialData = new SpecialWorldDataService();
        CurrentWorld.SpecialWorlds = new Dictionary<string, SpecialWorldCatalog>
        {
            ["underworld"] = specialData.LoadUnderworld(SourceResourcePath("Dat", "UnderworldMapPool.dat")),
            ["phoenix"] = specialData.LoadPhoenix(SourceResourcePath("Dat", "PhoenixMapPool.dat"))
        };
        CurrentWorld.LoadErrors = loadErrors;
        CurrentMap = CurrentWorld.Maps[selectedRoot];
        CurrentMapPath = paths[selectedRoot];
        _editing.ClearHistory();
        _bulkUndo.Clear();
        _globalUndo.Clear();
        return CurrentWorld;
    }

    private static short ParseSectorId(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path) ?? string.Empty);
        var match = System.Text.RegularExpressions.Regex.Match(directory.Name, "^sector-(\\d+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && short.TryParse(match.Groups[1].Value, out var sectorId)) return sectorId;
        return ParseLegacySectorId(path);
    }

    private string SourceResourcePath(params string[] parts)
    {
        var reference = Path.Combine(new[] { ReferenceResourceRoot }.Concat(parts).ToArray());
        return File.Exists(reference) ? reference : Path.Combine(new[] { ResourceRoot }.Concat(parts).ToArray());
    }

    public bool SwitchWorldLevel(int worldId)
    {
        if (CurrentWorld is null) return false;
        var layout = CurrentWorld.WorldLayouts.FirstOrDefault(item => item.WorldId == worldId);
        if (layout is null) return false;
        var root = MapsWorldDataService.ChooseRoot(layout, CurrentWorld.Paths.Keys.ToArray());
        if (root <= 0) return false;
        var assembly = MapsWorldDataService.BuildAssembly(CurrentWorld.WorldLayouts, CurrentWorld.Paths.Keys.ToArray(), root);
        if (assembly is null) return false;
        CurrentWorld.SwitchLevel(assembly, root);
        CurrentWorld.SetActiveMap(root);
        CurrentMap = CurrentWorld.Maps[root];
        CurrentMapPath = CurrentWorld.Paths[root];
        _editing.ClearHistory();
        _bulkUndo.Clear();
        _globalUndo.Clear();
        return true;
    }

    public void PreloadWorld(IProgress<WorldLoadProgress>? progress = null)
    {
        if (CurrentWorld is null) throw new InvalidOperationException("No hay un mundo cargado.");
        var paths = CurrentWorld.Paths.ToArray();
        var errors = CurrentWorld.LoadErrors.ToList();
        var initialErrorCount = errors.Count;
        var total = paths.Length + initialErrorCount;
        for (var index = 0; index < paths.Length; index++)
        {
            var (mapId, path) = paths[index];
            try
            {
                _ = CurrentWorld.Maps[mapId];
                progress?.Report(new WorldLoadProgress(total, index + 1, Path.GetFileName(path), null));
            }
            catch (Exception error)
            {
                CurrentWorld.MarkMapUnavailable(mapId);
                var message = $"{Path.GetFileName(path)}: {error.Message}";
                if (!errors.Contains(message)) errors.Add(message);
                progress?.Report(new WorldLoadProgress(total, index + 1, Path.GetFileName(path), error.Message));
            }
        }
        for (var index = 0; index < initialErrorCount; index++)
        {
            var error = errors[index];
            progress?.Report(new WorldLoadProgress(total, paths.Length + index + 1, error.Split(':')[0], error));
        }
        CurrentWorld.LoadErrors = errors;
        CurrentWorld.KeepAllLoaded = true;
    }

    private static bool IsGlobalWorldFileName(string fileName) =>
        fileName.Equals("world.csm", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("mapaGlobal.csm", StringComparison.OrdinalIgnoreCase);

    private static short ParseLegacySectorId(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return short.TryParse(digits, out var id) ? id : (short)0;
    }

    public void NewMap()
    {
        CurrentMap = new WorldSector();
        for (var x = 1; x <= WorldSector.Width; x++)
        for (var y = 1; y <= WorldSector.Height; y++)
            CurrentMap.Blocks[x, y].Graphics[0].GraphicIndex = 1;
        CurrentMap.Info.Name = "Nuevo Mapa";
        CurrentMap.Info.Music = "0";
        CurrentMap.Info.MapVersion = 0;
        CurrentMap.Info.PlayerKilling = true;
        CurrentMap.Info.Terrain = "BOSQUE";
        CurrentMap.Info.Zone = "CAMPO";
        CurrentMap.Info.Restriction = "No";
        CurrentMap.Info.Changed = false;
        CurrentMapPath = null;
        CurrentWorld = null;
        _editing.ClearHistory();
        _bulkUndo.Clear();
        _globalUndo.Clear();
    }

    public void SetGraphic(int x, int y, int layer, int graphicIndex)
    {
        var map = CurrentMapRequired();
        if (UseUndo) _editing.SetGraphic(map, x, y, layer, graphicIndex);
        else
        {
            if (!InDomain(map, x, y) || layer is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(x));
            map.Blocks[x, y].Graphics[layer].GraphicIndex = graphicIndex; map.Info.Changed = true;
        }
    }
    public void ClearTileGraphics(int x, int y) => ExecuteBulk(map =>
    {
        ValidateTile(map, x, y);
        for (var layer = 0; layer < 4; layer++) map.Blocks[x, y].Graphics[layer].GraphicIndex = 0;
        map.Info.Changed = true;
    });
    public void PasteClipboard(MapClipboardService clipboard, int x, int y) => ExecuteBulk(map => clipboard.Paste(map, x, y));
    public int PasteHouse(string houseMapPath, int x, int y) => ExecuteBulk(map =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(houseMapPath);
        if (!File.Exists(houseMapPath)) throw new FileNotFoundException("No existe el mapa de la casa.", houseMapPath);
        var source = Path.GetExtension(houseMapPath).Equals(".csm", StringComparison.OrdinalIgnoreCase)
            ? _csmMaps.Load(houseMapPath)
            : new LegacySectorFileService().LoadV3(houseMapPath);
        return new MapRegionCopyService().Copy(source, new MapRegion(65, 23, 74, 30), map, x, y);
    });
    public void CutClipboard(MapClipboardService clipboard, TileSelection selection) => ExecuteBulk(map => clipboard.Cut(map, selection));
    public int ToggleSelectionBlocked(TileSelection selection) => ExecuteBulk(map =>
    {
        var normalized = selection.Normalize();
        var minX = Math.Max(normalized.StartX, (int)map.CsmSectorSize.XMin);
        var maxX = Math.Min(normalized.EndX, (int)map.CsmSectorSize.XMax);
        var minY = Math.Max(normalized.StartY, (int)map.CsmSectorSize.YMin);
        var maxY = Math.Min(normalized.EndY, (int)map.CsmSectorSize.YMax);
        var changed = 0;
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var block = map.Blocks[x, y];
            block.Blocked = block.Blocked > 0 ? (byte)0 : (byte)0xF;
            changed++;
        }
        map.Info.Changed = changed > 0;
        return changed;
    });

    public int ApplyBlockMask(TileSelection selection, byte mask) => ExecuteBulk(map =>    {
        var normalized = selection.Normalize();
        var minX = Math.Max(normalized.StartX, (int)map.CsmSectorSize.XMin);
        var maxX = Math.Min(normalized.EndX, (int)map.CsmSectorSize.XMax);
        var minY = Math.Max(normalized.StartY, (int)map.CsmSectorSize.YMin);
        var maxY = Math.Min(normalized.EndY, (int)map.CsmSectorSize.YMax);
        var changed = 0;
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            map.Blocks[x, y].Blocked = mask;
            changed++;
        }
        map.Info.Changed = changed > 0;
        return changed;
    });
    public int FixCoasts(TileSelection selection) => ExecuteBulk(map => new CoastFixService().FixSelection(map, selection));
    public void ClearSelectionGraphics(TileSelection selection) => ExecuteBulk(map =>
    {
        var normalized = selection.Normalize();
        var minX = Math.Max(normalized.StartX, (int)map.CsmSectorSize.XMin);
        var maxX = Math.Min(normalized.EndX, (int)map.CsmSectorSize.XMax);
        var minY = Math.Max(normalized.StartY, (int)map.CsmSectorSize.YMin);
        var maxY = Math.Min(normalized.EndY, (int)map.CsmSectorSize.YMax);
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
            for (var layer = 0; layer < 4; layer++) map.Blocks[x, y].Graphics[layer].GraphicIndex = 0;
        map.Info.Changed = true;
    });
    public bool Undo()
    {
        var map = CurrentMapRequired();
        if (!UseUndo) return false;
        if (_globalUndo.First is { } globalSnapshot && CurrentWorld is not null)
        {
            _globalUndo.RemoveFirst();
            globalSnapshot.Value.Restore(CurrentWorld);
            return true;
        }
        if (_bulkUndo.First is { } snapshot)
        {
            _bulkUndo.RemoveFirst(); snapshot.Value.Restore(map); return true;
        }
        return _editing.Undo(map);
    }
    public void ClearLayer(int layer) => ExecuteBulk(map => _bulk.ClearLayer(map, layer));
    public int PlaceBlock(BlockPresetType preset, BlockPreset item, int x, int y) =>
        ExecuteBulk(map => new BlockPlacementService().Place(map, preset, item, x, y));
    public void LockBorders() => ExecuteBulk(_surfaces.LockBorders);
    public int RemoveNpcs() => ExecuteBulk(map => _cleanup.RemoveNpcs(map));
    public int RemoveObjects() => ExecuteBulk(map => _cleanup.RemoveObjects(map));
    public int RemoveTriggers() => ExecuteBulk(map => _cleanup.RemoveTriggers(map));
    public int RemoveExits() => ExecuteBulk(map => _cleanup.RemoveExits(map));
    public MapOptimizationResult Optimize(MapOptimizationOptions options) =>
        ExecuteBulk(map => _optimization.Optimize(map, Resources.Objects, options));
    public void ApplyMapInfo(SectorInfoValues values) => ExecuteBulk(map => _mapInfo.Apply(map, values));
    public void SetMusic(string music) => ExecuteBulk(map =>
        _mapInfo.Apply(map, new SectorInfoValues(map.Info.Name, music, map.Info.MapVersion, map.Info.PlayerKilling,
            map.Info.MagicDisabled, map.Info.InvisibilityDisabled, map.Info.ResurrectionDisabled, map.Info.NoEncryptMap,
            map.Info.Terrain, map.Info.Zone, map.Info.Restriction, map.Info.Backup, map.Info.Light)));
    public int ApplyAdjacentTransfers(IReadOnlyList<AdjacentTransfer> transfers) =>
        ExecuteBulk(map => _adjacentTransfers.Apply(map, transfers));

    public int CopyRegionToMap(MapRegion region, string destinationPath, int destinationX, int destinationY)
    {
        var source = CurrentMapRequired();
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var fullPath = Path.GetFullPath(destinationPath);
        var destination = Path.GetExtension(fullPath).Equals(".csm", StringComparison.OrdinalIgnoreCase)
            ? _csmMaps.Load(fullPath)
            : new LegacySectorFileService().LoadV3(fullPath);
        var copied = new MapRegionCopyService().Copy(source, region, destination, destinationX, destinationY);
        if (Path.GetExtension(fullPath).Equals(".csm", StringComparison.OrdinalIgnoreCase)) _csmMaps.Save(destination, fullPath);
        else new LegacySectorFileService().SaveV3(destination, fullPath);
        return copied;
    }

    public IReadOnlyList<StitchResult> StitchAdjacentMaps(IReadOnlyCollection<StitchSide> sides)
    {
        var source = CurrentMapRequired();
        if (string.IsNullOrWhiteSpace(CurrentMapPath)) throw new InvalidOperationException("El mapa actual no tiene ruta.");
        var directory = Path.GetDirectoryName(CurrentMapPath)!;
        var stitcher = new AdjacentMapStitchService();
        var results = new List<StitchResult>();
        foreach (var side in sides.Distinct())
        {
            var map = stitcher.DetectNeighborMap(source, side);
            if (map == 0) { results.Add(new StitchResult(side, 0, 0, false)); continue; }
            var path = Directory.EnumerateFiles(directory, $"mapa{map}.csm", SearchOption.TopDirectoryOnly)
                .FirstOrDefault() ?? Directory.EnumerateFiles(directory, $"map{map}.csm", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (path is null) { results.Add(new StitchResult(side, map, 0, false)); continue; }
            var destination = Path.GetExtension(path).Equals(".csm", StringComparison.OrdinalIgnoreCase) ? _csmMaps.Load(path) : new LegacySectorFileService().LoadV3(path);
            var copied = stitcher.CopySide(source, destination, side);
            if (Path.GetExtension(path).Equals(".csm", StringComparison.OrdinalIgnoreCase)) _csmMaps.Save(destination, path);
            else new LegacySectorFileService().SaveV3(destination, path);
            results.Add(new StitchResult(side, map, copied, true));
        }
        return results;
    }

    private void ExecuteBulk(Action<WorldSector> operation)
    {
        ExecuteBulk<int>(map => { operation(map); return 0; });
    }

    private T ExecuteBulk<T>(Func<WorldSector, T> operation)
    {
        var map = CurrentMapRequired();
        var snapshot = WorldSectorSnapshot.Capture(map);
        var result = operation(map);
        if (UseUndo)
        {
            _bulkUndo.AddFirst(snapshot);
            while (_bulkUndo.Count > 30) _bulkUndo.RemoveLast();
        }
        _editing.ClearHistory();
        return result;
    }

    public void SaveMap(string path)
    {
        var map = CurrentMapRequired();
        if (Path.GetExtension(path).Equals(".csm", StringComparison.OrdinalIgnoreCase))
            _csmMaps.Save(map, path);
        else
            new LegacySectorFileService().SaveV3(map, path);
        CurrentMapPath = Path.GetFullPath(path);
    }

    private WorldSector CurrentMapRequired() => CurrentMap ?? throw new InvalidOperationException("No hay un mapa cargado.");
}
