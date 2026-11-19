using System.Threading;

namespace GtaTransformationHub.Services;

/// <summary>
/// Compatibility facade for older transaction call sites. Every operation delegates
/// to the single process-wide mutation semaphore, so it cannot create a second lock.
/// </summary>
internal static class DeploymentLock
{
    internal static Task WaitAsync(CancellationToken cancellationToken) =>
        GameMutationGate.Gate.WaitAsync(cancellationToken);

    internal static void Release() =>
        GameMutationGate.Gate.Release();
}
