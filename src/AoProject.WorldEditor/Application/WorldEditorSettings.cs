using System.Globalization;
using AoProject.WorldEditor.Infrastructure;
using AoProject.WorldEditor.Domain.Rendering;

namespace AoProject.WorldEditor.Application;

internal sealed class WorldEditorSettings
{
    public int MaxGraphics { get; init; } = 49488;
    public string LastMap { get; set; } = string.Empty;
    public int TransferObject { get; init; } = 378;
    public bool AutoCaptureTransfers { get; set; } = true;
    public bool AutoCaptureSurfaces { get; set; } = true;
    public bool UseUndo { get; set; } = true;
    public int LastX { get; set; } = 50;
    public int LastY { get; set; } = 50;
    public bool ShowLayer2 { get; set; } = true;
    public bool ShowLayer3 { get; set; } = true;
    public bool ShowLayer4 { get; set; } = true;
    public bool ShowGrid { get; set; }
    public bool ShowTriggers { get; set; }
    public bool ShowBlocked { get; set; }
    public bool ShowTransfers { get; set; }
    public bool ShowObjects { get; set; } = true;
    public bool ShowNpcs { get; set; } = true;
    public bool ShowParticles { get; set; }
    public bool ShowLights { get; set; }
    public bool ShowRoofFade { get; set; } = true;
    /// <summary>VB6 AlphaTecho ("Techos transparentes"); runtime-only there, persisted here.</summary>
    public bool AlphaRoofs { get; set; }
    public int ScreenTilesX { get; init; } = 14;
    public int ScreenTilesY { get; init; } = 16;
    public int ClientHeight { get; init; } = 30;
    public int ClientWidth { get; init; } = 40;
    public bool ShowFrame { get; init; }

    public void ApplyTo(WorldRenderOptions options)
    {
        options.ShowLayer1 = true; options.ShowLayer2 = ShowLayer2; options.ShowLayer3 = ShowLayer3; options.ShowLayer4 = ShowLayer4;
        options.ShowGrid = ShowGrid; options.ShowTriggers = ShowTriggers; options.ShowBlocked = ShowBlocked; options.ShowTransfers = ShowTransfers;
        options.ShowObjects = ShowObjects; options.ShowNpcs = ShowNpcs; options.ShowParticles = ShowParticles; options.ShowLights = ShowLights; options.ShowRoofFade = ShowRoofFade; options.AlphaRoofs = AlphaRoofs;
    }

    public void UpdateFrom(WorldRenderOptions options)
    {
        ShowLayer2 = options.ShowLayer2; ShowLayer3 = options.ShowLayer3; ShowLayer4 = options.ShowLayer4; ShowGrid = options.ShowGrid;
        ShowTriggers = options.ShowTriggers; ShowBlocked = options.ShowBlocked; ShowTransfers = options.ShowTransfers; ShowObjects = options.ShowObjects; ShowNpcs = options.ShowNpcs;
        ShowParticles = options.ShowParticles; ShowLights = options.ShowLights; ShowRoofFade = options.ShowRoofFade; AlphaRoofs = options.AlphaRoofs;
    }

    public static WorldEditorSettings Load(string path)
    {
        if (!File.Exists(path)) return new WorldEditorSettings();
        var ini = IniDocument.Load(path);
        var last = ini.Get("MOSTRAR", "LastPos").Split('-');
        return new WorldEditorSettings
        {
            MaxGraphics = Value(ini, "INDEX", "MaxGrhs", 49488),
            LastMap = ini.Get("PATH", "UltimoMapa"),
            TransferObject = Value(ini, "CONFIGURACION", "ObjTranslado", 378),
            AutoCaptureTransfers = Flag(ini, "CONFIGURACION", "AutoCapturarTrans", true),
            AutoCaptureSurfaces = Flag(ini, "CONFIGURACION", "AutoCapturarSup", true),
            UseUndo = Flag(ini, "CONFIGURACION", "UtilizarDeshacer", true),
            LastX = last.Length > 0 ? Parse(last[0], 50) : 50,
            LastY = last.Length > 1 ? Parse(last[1], 50) : 50,
            ShowLayer2 = Flag(ini, "MOSTRAR", "Capa2", true), ShowLayer3 = Flag(ini, "MOSTRAR", "Capa3", true),
            ShowLayer4 = Flag(ini, "MOSTRAR", "Capa4", true), ShowGrid = Flag(ini, "MOSTRAR", "Grilla", false),
             ShowTriggers = Flag(ini, "MOSTRAR", "Triggers", false), ShowBlocked = Flag(ini, "MOSTRAR", "Bloqueos", false),
            ShowTransfers = Flag(ini, "MOSTRAR", "Translados", false), ShowObjects = Flag(ini, "MOSTRAR", "Objetos", true),
             ShowNpcs = Flag(ini, "MOSTRAR", "NPCs", true), ShowParticles = Flag(ini, "MOSTRAR", "Particulas", false),
              ShowLights = Flag(ini, "MOSTRAR", "Luces", false), ShowRoofFade = Flag(ini, "MOSTRAR", "FundidoTechos", true), AlphaRoofs = Flag(ini, "MOSTRAR", "AlphaTechos", false), ScreenTilesX = Value(ini, "MOSTRAR", "PantallaX", 14),
            ScreenTilesY = Value(ini, "MOSTRAR", "PantallaY", 16), ClientHeight = Value(ini, "MOSTRAR", "ClienteHeight", 30),
            ClientWidth = Value(ini, "MOSTRAR", "ClienteWidth", 40), ShowFrame = Flag(ini, "MOSTRAR", "Marco", false)
        };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllLines(path, new[]
        {
            "[INDEX]", $"MaxGrhs={MaxGraphics}", "", "[PATH]", $"UltimoMapa={LastMap}", "",
            "[CONFIGURACION]", $"ObjTranslado={TransferObject}", $"AutoCapturarTrans={(AutoCaptureTransfers ? 1 : 0)}",
            $"AutoCapturarSup={(AutoCaptureSurfaces ? 1 : 0)}", $"UtilizarDeshacer={(UseUndo ? 1 : 0)}", "",
            "[MOSTRAR]", $"LastPos={LastX}-{LastY}", $"Capa2={(ShowLayer2 ? 1 : 0)}", $"Capa3={(ShowLayer3 ? 1 : 0)}",
            $"Capa4={(ShowLayer4 ? 1 : 0)}", $"Grilla={(ShowGrid ? 1 : 0)}", $"Triggers={(ShowTriggers ? 1 : 0)}",
            $"Bloqueos={(ShowBlocked ? 1 : 0)}", $"Translados={(ShowTransfers ? 1 : 0)}", $"Objetos={(ShowObjects ? 1 : 0)}",
              $"NPCs={(ShowNpcs ? 1 : 0)}", $"Particulas={(ShowParticles ? 1 : 0)}", $"Luces={(ShowLights ? 1 : 0)}", $"FundidoTechos={(ShowRoofFade ? 1 : 0)}", $"AlphaTechos={(AlphaRoofs ? 1 : 0)}",
             $"PantallaX={ScreenTilesX}", $"PantallaY={ScreenTilesY}",
            $"ClienteHeight={ClientHeight}", $"ClienteWidth={ClientWidth}", $"Marco={(ShowFrame ? 1 : 0)}"
        });
    }

    private static bool Flag(IniDocument ini, string section, string key, bool fallback) => Value(ini, section, key, fallback ? 1 : 0) != 0;
    private static int Value(IniDocument ini, string section, string key, int fallback) => Parse(ini.Get(section, key), fallback);
    private static int Parse(string value, int fallback) => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;
}
