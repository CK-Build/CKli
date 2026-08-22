using CK.Core;
using CK.PerfectEvent;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

/// <summary>
/// Abstract factory of cached <see cref="RepoBuilder"/> per <see cref="Repo"/>.
/// Currently always associates a basic <see cref="RepoBuilder"/>.
/// </summary>
public sealed class RepositoryBuilderPlugin : PrimaryRepoPlugin<RepoBuilder>
{
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly PerfectEventSender<CoreBuildEventArgs> _onCoreBuild;

    // This cache is common to all Repo in all World: this caches the Content Sha of
    // successfully passed tests. All RepoBuilder use it.
    // This is in the $Local (not in the git repository) to allow tests to run on each machine.
    // There is currently no housekeeping.
    internal LocalStringCache? _shaTestRunCache;

    /// <summary>
    /// Initializes a new builder plugin.
    /// </summary>
    /// <param name="primaryContext">The CKli context.</param>
    /// <param name="artifactHandler">The artifact handler plugin.</param>
    public RepositoryBuilderPlugin( PrimaryPluginContext primaryContext, ArtifactHandlerPlugin artifactHandler )
        : base( primaryContext )
    {
        _artifactHandler = artifactHandler;
        _onCoreBuild = new PerfectEventSender<CoreBuildEventArgs>();
    }

    /// <summary>
    /// Raised right before the build by the <see cref="RepoBuilder"/>.
    /// <para>
    /// The working folder is ready (the "nuget.config" file contains the $"Local/&lt;world name&gt;/NuGet" local feed)
    /// and will be restored after the build.
    /// </para>
    /// </summary>
    public PerfectEvent<CoreBuildEventArgs> OnCoreBuild => _onCoreBuild.PerfectEvent;

    /// <inheritdoc />
    protected override RepoBuilder Create( IActivityMonitor monitor, Repo repo )
    {
        _shaTestRunCache ??= new LocalStringCache( repo.World.Name, "TestRun.Sha" );
        return new RepoBuilder( repo, this, _artifactHandler, _artifactHandler.Get( monitor, repo ) );
    }

    internal async Task<(bool,BuildResult?)> RaiseOnCoreBuildAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommitBuildInfo buildInfo,
                                                                    string outputPath,
                                                                    bool runTest,
                                                                    CancellationToken cancellation )
    {
        CoreBuildEventArgs? e = null;
        if( _onCoreBuild.HasHandlers )
        {
            using( monitor.OpenInfo( "Raising CoreBuild event." ) )
            {
                bool eventError = false;
                using( monitor.OnError( () => eventError = true ) )
                {
                    e = new CoreBuildEventArgs( monitor, context, buildInfo, outputPath, runTest, cancellation );
                    if( !await _onCoreBuild.SafeRaiseAsync( monitor, e, cancellation ).ConfigureAwait( false )
                        || eventError )
                    {
                        monitor.CloseGroup( $"OnCoreBuild event handling failed." );
                        return (false, null);
                    }
                }
            }
        }
        else
        {
            monitor.Info( $"No listener to the CoreBuild event." );
        }
        return (true, e?.ResultHook);
    }
}
