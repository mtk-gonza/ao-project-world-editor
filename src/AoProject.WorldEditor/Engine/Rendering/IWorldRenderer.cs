using System.Drawing;

namespace AoProject.WorldEditor.Engine.Rendering;

/// <summary>Backend-independent contract for the WorldEditor render surface.</summary>
internal interface IWorldRenderer : IDisposable
{
    bool IsReady { get; }
    void Resize(int width, int height);
    void Render(Color clearColor);
}
