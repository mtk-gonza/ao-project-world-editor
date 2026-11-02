using AoProject.WorldEditor.Infrastructure;

namespace AoProject.WorldEditor.Application;

internal readonly record struct ResourceIndexRefreshResult(
    int Graphics,
    int Surfaces,
    int Objects,
    int Npcs,
    int Triggers,
    int Heads,
    int Bodies);

/// <summary>Read-only equivalent of frmActualizarIndices: reloads each index from resources.</summary>
internal sealed class ResourceIndexRefreshService
{
    public (WorldEditorResourceCatalog Catalog, ResourceIndexRefreshResult Result) Reload(string resourceRoot, string? indexRoot = null)
    {
        var catalog = WorldEditorResourceCatalog.Load(resourceRoot, indexRoot);
        return (catalog, new ResourceIndexRefreshResult(catalog.Graphics.Count - 1, catalog.Surfaces.Count - 1,
            catalog.Objects.Count - 1, catalog.Npcs.Count - 1, catalog.Triggers.Count, catalog.Heads.Length - 1, catalog.Bodies.Length - 1));
    }
}
