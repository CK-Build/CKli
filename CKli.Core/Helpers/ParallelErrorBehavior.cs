namespace CKli.Core;

/// <summary>
/// Drives <see cref="ActivityMonitorAsyncPool.ParallelAsync{T}"/> behavior.
/// </summary>
public enum ParallelErrorBehavior
{
    /// <summary>
    /// Ignore error. Other tasks are started and continue independently.
    /// </summary>
    Ignore,

    /// <summary>
    /// On error, let the currently started tasks end but avoid launching new tasks.
    /// </summary>
    SoftStop,

    /// <summary>
    /// On error, immediately cancel other tasks.
    /// </summary>
    HardStop
}
