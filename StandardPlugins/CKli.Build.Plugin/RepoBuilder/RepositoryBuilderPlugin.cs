using CK.Core;
using CK.PerfectEvent;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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
    readonly ImmutableArray<string> _deleteBeforeBuild;

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
        // <Build DeleteBeforeBuild="$StObjGen;*.g.cs" />: entries that a previous build produced and that
        // this one must produce again instead of reusing. CKli knows nothing about a World's code
        // generators, so this is empty by default.
        _deleteBeforeBuild = ParseDeleteBeforeBuild( (string?)primaryContext.Configuration.XElement.Attribute( XNames.DeleteBeforeBuild ) );
    }

    /// <summary>
    /// Deletes the files and folders configured by &lt;Build DeleteBeforeBuild="..." /&gt; in the repository's
    /// working folder. They hold content produced by a previous build - possibly by another project of this
    /// very solution - that this build must produce again instead of reusing.
    /// <para>
    /// Entries follow the ".gitignore" rule: an entry without any '/' is matched at any depth (this is what a
    /// per project folder such as "$StObjGen" needs), an entry containing a '/' is anchored at the working
    /// folder. The name part can use the '*' and '?' wildcards.
    /// </para>
    /// <para>
    /// Only git ignored content can be deleted. A build must produce the artifacts of the commit that it tags:
    /// deleting tracked content would build something else, and the hard reset done after the build would
    /// restore the file and hide it. An entry matching non ignored content fails the build.
    /// </para>
    /// <para>
    /// Folders are deleted rather than emptied: they are git ignored, so this is exactly what a fresh clone
    /// looks like (and the hard reset done after the build removes empty folders anyway).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository that is about to be built.</param>
    /// <returns>True on success, false otherwise.</returns>
    internal bool DeleteBeforeBuild( IActivityMonitor monitor, Repo repo )
    {
        if( _deleteBeforeBuild.Length == 0 ) return true;
        var root = repo.WorkingFolder;
        var git = repo.GitRepository.Repository;
        bool success = true;
        using var _ = monitor.OpenTrace( $"Deleting the content configured by '{XNames.DeleteBeforeBuild}'." );
        foreach( var e in _deleteBeforeBuild )
        {
            int iSep = e.LastIndexOf( '/' );
            var parent = iSep < 0 ? root : root.Combine( e.Substring( 0, iSep ) );
            var pattern = iSep < 0 ? e : e.Substring( iSep + 1 );
            var depth = iSep < 0 ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            if( !Directory.Exists( parent ) ) continue;
            // EnumerateFileSystemEntries handles both kinds at once: an entry doesn't have to say whether it
            // denotes a file or a folder.
            foreach( var p in Directory.EnumerateFileSystemEntries( parent, pattern, depth ) )
            {
                var relative = new NormalizedPath( p ).RemovePrefix( root );
                if( !git.Ignore.IsPathIgnored( relative ) )
                {
                    monitor.Error( $"""
                        '{XNames.DeleteBeforeBuild}' entry '{e}' matches '{relative}' that is not git ignored.
                        A build must produce the artifacts of the commit that it tags, so only git ignored
                        content can be deleted. Add it to the ".gitignore" file or remove the entry from
                        <Build {XNames.DeleteBeforeBuild}="..." />.
                        """ );
                    success = false;
                    continue;
                }
                monitor.Trace( $"Deleting '{relative}'." );
                if( !(Directory.Exists( p )
                        ? FileHelper.DeleteFolder( monitor, p )
                        : FileHelper.DeleteFile( monitor, p )) )
                {
                    success = false;
                }
            }
        }
        if( !success )
        {
            monitor.Error( $"Unable to delete the content configured by '{XNames.DeleteBeforeBuild}': failing the build." );
        }
        return success;
    }

    // An entry must stay inside the working folder: it is a relative path and cannot climb out of it.
    // Rooted and "..' entries are rejected here rather than at delete time so that a mistake in the World
    // file is reported once, when the plugin is instantiated.
    static ImmutableArray<string> ParseDeleteBeforeBuild( string? configuration )
    {
        if( string.IsNullOrWhiteSpace( configuration ) ) return ImmutableArray<string>.Empty;
        var b = ImmutableArray.CreateBuilder<string>();
        foreach( var raw in configuration.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
        {
            var e = new NormalizedPath( raw );
            if( e.IsRooted || e.Parts.Contains( ".." ) )
            {
                Throw.ArgumentException( nameof( configuration ),
                                         $"""
                                          Invalid <Build {XNames.DeleteBeforeBuild}="..." /> entry '{raw}': an entry must be
                                          relative to the repository's working folder and cannot contain '..'.
                                          """ );
            }
            b.Add( e.Path );
        }
        return b.DrainToImmutable();
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
