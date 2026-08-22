using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System.Threading;

namespace CKli.Build.Plugin;

/// <summary>
/// This event is raised by <see cref="RepoBuilder.BuildAsync"/>.
/// It wraps the CommitBuildInfo that describes the build that is about to be ran in
/// the checked out <see cref="CommitBuildInfo.Repo"/>.
/// <para>
/// This can hook the build thanks to the <see cref="ResultHook"/>.
/// </para>
/// </summary>
public sealed class CoreBuildEventArgs : WorldEventArgs
{
    readonly CancellationToken _cancellation;
    readonly CommitBuildInfo _buildInfo;
    readonly string _outputPath;
    readonly bool _runTest;

    internal CoreBuildEventArgs( IActivityMonitor monitor,
                                 CKliEnv context,
                                 CommitBuildInfo buildInfo,
                                 string outputPath,
                                 bool runTest,
                                 CancellationToken cancellation )
        : base( monitor, context, buildInfo.Repo.World )
    {
        _cancellation = cancellation;
        _buildInfo = buildInfo;
        _outputPath = outputPath;
        _runTest = runTest;
    }

    /// <summary>
    /// Gets or sets the build result to consider.
    /// When setting this to a non null result, the build/test/package steps are skipped.
    /// </summary>
    public BuildResult? ResultHook { get; set; }

    /// <summary>
    /// Gets the <see cref="CommitBuildInfo"/>.
    /// </summary>
    public CommitBuildInfo BuildInfo => _buildInfo;

    /// <summary>
    /// Gets the temporary output folder for artifacts.
    /// </summary>
    public string OutputPath => _outputPath;

    /// <summary>
    /// Gets whether repository's tests must be run or not.
    /// </summary>
    public bool RunTest => _runTest;

    /// <summary>
    /// Get the cancellation token of the build operation.
    /// </summary>
    public CancellationToken Cancellation => _cancellation;

}
