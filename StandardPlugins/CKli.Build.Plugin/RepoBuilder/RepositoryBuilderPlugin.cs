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
    /// The name of the <see cref="LocalStringCache"/> of the commit contents whose tests have successfully run.
    /// </summary>
    internal const string ShaTestRunCacheName = "TestRun.Sha";

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
        World.Events.PluginInfo += PluginInfoRequested;
        World.Events.CreateLTS.Sync += LTSCreated;
        // <Build DeleteBeforeBuild="$StObjGen;*.g.cs" />: entries that a previous build produced and that
        // this one must produce again instead of reusing. CKli knows nothing about a World's code
        // generators, so this is empty by default.
        _deleteBeforeBuild = SplitDeleteBeforeBuild( (string?)primaryContext.Configuration.XElement.Attribute( XNames.DeleteBeforeBuild ) );
    }

    // The "TestRun.Sha" cache records the commit contents whose tests have successfully run: it moves to the new
    // Long Term Support world, the one that keeps the code it has been computed on.
    void LTSCreated( IActivityMonitor monitor, CreateLTSEventArgs e )
    {
        var current = LocalStringCache.GetFilePath( World.Name, ShaTestRunCacheName );
        if( File.Exists( current ) )
        {
            var target = LocalStringCache.GetFilePath( e.LTSWorldName, ShaTestRunCacheName );
            e.AddCreationStep( m => FileHelper.MoveFile( m, current, target ) );
        }
    }

    void PluginInfoRequested( PluginInfoEventArgs e )
    {
        var s = e.ScreenType;
        IRenderable message;
        if( _deleteBeforeBuild.Length > 0 )
        {
            message = s.Text( nameof( XNames.DeleteBeforeBuild ), foreColor: ConsoleColor.Green )
                       .AddRight( s.Text( $"""is "{_deleteBeforeBuild.Concatenate( ";" )}": these git ignored files and folders are deleted from a repository's working folder before it is built.""" )
                                   .Box( marginLeft: 1 ) );
        }
        else
        {
            message = s.Text( nameof( XNames.DeleteBeforeBuild ), foreColor: ConsoleColor.DarkGray )
                       .AddRight( s.Text( """is not set (the default): nothing is deleted before a build. Set it to the git ignored content that a previous build generates (such as "$StObjGen") so that it is produced again instead of being reused.""" )
                                   .Box( marginLeft: 1 ) );
        }
        e.AddMessage( PrimaryPluginContext, message );
    }

    /// <inheritdoc />
    protected override Task<bool?> OnPluginSetAsync( IActivityMonitor monitor,
                                                     PluginInfo? pluginInfo,
                                                     string attributeName,
                                                     string? attributeValue )
    {
        bool? result = null;
        if( attributeName.Equals( XNames.DeleteBeforeBuild.LocalName, StringComparison.OrdinalIgnoreCase ) )
        {
            // Refuse an invalid entry rather than persisting a configuration that fails every subsequent build.
            result = CheckEntries( monitor, SplitDeleteBeforeBuild( attributeValue ) )
                     && PrimaryPluginContext.Configuration.SetAttribute( monitor, XNames.DeleteBeforeBuild, attributeValue );
        }
        return Task.FromResult( result );
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
        using var _ = monitor.OpenTrace( $"Deleting the content configured by '{XNames.DeleteBeforeBuild}'." );
        // "ckli plugin set" refuses an invalid entry, but the World definition file can be edited by hand.
        if( !CheckEntries( monitor, _deleteBeforeBuild ) ) return false;
        var root = repo.WorkingFolder;
        var git = repo.GitRepository.Repository;
        bool success = true;
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

    // Splitting cannot fail: the constructor has no monitor, and a typo in the World definition file must not
    // fail the plugin instantiation (that degrades the World to "working without plugins"). Validation is done
    // where a monitor is available: "ckli plugin set" refuses an invalid value, and DeleteBeforeBuild fails the
    // build of a World file that was edited by hand.
    static ImmutableArray<string> SplitDeleteBeforeBuild( string? configuration )
    {
        if( string.IsNullOrWhiteSpace( configuration ) ) return ImmutableArray<string>.Empty;
        var b = ImmutableArray.CreateBuilder<string>();
        foreach( var raw in configuration.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
        {
            b.Add( new NormalizedPath( raw ).Path );
        }
        return b.DrainToImmutable();
    }

    // An entry must stay inside the working folder: it is a relative path and cannot climb out of it.
    static bool CheckEntries( IActivityMonitor monitor, ImmutableArray<string> entries )
    {
        bool valid = true;
        foreach( var e in entries )
        {
            if( e.StartsWith( '/' ) || new NormalizedPath( e ).Parts.Contains( ".." ) )
            {
                monitor.Error( $"""
                    Invalid '{XNames.DeleteBeforeBuild}' entry '{e}': an entry is relative to the repository's
                    working folder and cannot be rooted nor contain '..'.
                    """ );
                valid = false;
            }
        }
        return valid;
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
        _shaTestRunCache ??= new LocalStringCache( repo.World.Name, ShaTestRunCacheName );
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
