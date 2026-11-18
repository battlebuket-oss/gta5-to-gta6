namespace GtaTransformationHub.Services;

/// <summary>
/// One process-wide lock shared by deployment, removal, recovery and rollback.
/// </summary>
internal static class GameMutationGate
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}
