using CK.Core;
using CKli.VersionTag.Plugin;
using System.Threading;

namespace CKli.Build.Plugin;

/// <summary>
/// This event is raised by <see cref="RepoBuilder.BuildAsync"/>.
/// It wraps the CommitBuildInfo that describes the build that is about to be ran in
/// the checked out <see cref="CommitBuildInfo.Repo"/>.
/// </summary>
public sealed class CoreBuildEventArgs : EventMonitoredArgs
{
    readonly CancellationToken _cancellation;

    internal CoreBuildEventArgs( IActivityMonitor monitor, CommitBuildInfo buildInfo, CancellationToken cancellation )
        : base( monitor )
    {
        BuildInfo = buildInfo;
        _cancellation = cancellation;
    }

    /// <summary>
    /// Gets the <see cref="CommitBuildInfo"/>.
    /// </summary>
    public CommitBuildInfo BuildInfo { get; }

    /// <summary>
    /// Get the cancellation token of the build operation.
    /// </summary>
    public CancellationToken Cancellation => _cancellation;

}
