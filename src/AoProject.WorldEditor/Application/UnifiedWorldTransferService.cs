using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal readonly record struct UnifiedWorldPosition(int X, int Y, int Z);

internal enum UnifiedWorldTransferKind
{
    Internal,
    CrossFloor,
    External
}

internal sealed record UnifiedWorldResolvedTransfer(
    UnifiedWorldTransferKind Kind,
    UnifiedWorldPosition Source,
    UnifiedWorldPosition? Target,
    short ExternalSectorId,
    short ExternalX,
    short ExternalY,
    int ExternalZ);

/// <summary>Runtime-facing resolver for global transfers, independent of legacy SectorId coordinates.</summary>
internal sealed class UnifiedWorldTransferService
{
    public UnifiedWorldResolvedTransfer? Resolve(UnifiedWorldBundle bundle, UnifiedWorldPosition source)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!bundle.Floors.TryGetValue(source.Z, out var floor)) return null;
        if (source.X < 0 || source.X >= floor.Width || source.Y < 0 || source.Y >= floor.Height) return null;
        var reference = floor.ExitIndex[floor.Offset(source.X, source.Y)];
        if (reference == 0) return null;
        if (reference < 1 || reference > floor.Exits.Count)
            throw new InvalidDataException("Referencia de transferencia fuera de rango.");
        var exit = floor.Exits[reference - 1];
        var kind = exit.Kind switch
        {
            UnifiedWorldExitKind.Internal => UnifiedWorldTransferKind.Internal,
            UnifiedWorldExitKind.CrossFloor => UnifiedWorldTransferKind.CrossFloor,
            _ => UnifiedWorldTransferKind.External
        };
        UnifiedWorldPosition? target = kind is UnifiedWorldTransferKind.Internal or UnifiedWorldTransferKind.CrossFloor
            ? new UnifiedWorldPosition(exit.TargetX, exit.TargetY, exit.TargetZ)
            : null;
        return new UnifiedWorldResolvedTransfer(kind, source, target,
            exit.ExternalSectorId, exit.ExternalX, exit.ExternalY, exit.ExternalZ);
    }
}
