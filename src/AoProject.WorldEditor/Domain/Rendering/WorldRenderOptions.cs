namespace AoProject.WorldEditor.Domain.Rendering;

internal sealed class WorldRenderOptions
{
    public bool ShowLayer1 { get; set; } = true;
    public bool ShowLayer2 { get; set; } = true;
    public bool ShowLayer3 { get; set; } = true;
    public bool ShowLayer4 { get; set; } = true;
    public bool ShowGrid { get; set; }
    public bool ShowBlocked { get; set; }
    public bool ShowTriggers { get; set; }
    public bool ShowTransfers { get; set; }
    public bool ShowObjects { get; set; } = true;
    public bool ShowNpcs { get; set; } = true;
    public bool ShowParticles { get; set; }
    public bool ShowLights { get; set; }
    public bool ShowRoofFade { get; set; } = true;
    /// <summary>VB6 AlphaTecho: additive blending for the roof layer.</summary>
    public bool AlphaRoofs { get; set; }

    public bool IsLayerVisible(int layer) => layer switch
    {
        0 => ShowLayer1,
        1 => ShowLayer2,
        2 => ShowLayer3,
        3 => ShowLayer4,
        4 or 5 or 6 or 7 => true,
        _ => false
    };
}
