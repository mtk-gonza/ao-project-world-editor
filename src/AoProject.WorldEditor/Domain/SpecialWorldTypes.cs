namespace AoProject.WorldEditor.Domain;

internal readonly record struct SpecialWorldPortal(
    string WorldId,
    short SourceMap,
    short SourceX,
    short SourceY,
    short DestinationMap,
    short DestinationX,
    short DestinationY);

internal sealed record SpecialWorldCatalog(
    IReadOnlyList<SpecialWorldPortal> Portals,
    IReadOnlyList<short> MapPool);
