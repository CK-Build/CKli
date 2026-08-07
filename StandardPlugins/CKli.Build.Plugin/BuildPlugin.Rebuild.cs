using CK.Core;
using CKli.Core;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    /// <summary>
    /// Tries to rebuild the oldest releases until a success.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="warnOnly"></param>
    /// <param name="runTest"></param>
    /// <param name="all"></param>
    /// <returns></returns>
    [Description( """
        Tries to rebuild the oldest releases until a success.
        Failing commits are tagged with a '+invalid' tag.
        """ )]
    [CommandPath( "maintenance rebuild old" )]
    public async Task<bool> RebuildOldAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             [Description( "Warns only: doesn't create a '+invalid' tag on the failing commit." )]
                                             bool warnOnly,
                                             [Description( "Runs unit tests. They must be successful." )]
                                             bool runTest,
                                             [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                                             bool all )
    {
        IReadOnlyList<Repo>? repos = all
                                      ? World.GetAllDefinedRepo( monitor )
                                      : World.GetAllDefinedRepo( monitor, context.CurrentDirectory );
        if( repos == null )
        {
            return false;
        }
        foreach( var repo in repos )
        {
            using( monitor.OpenInfo( $"Rebuilding old releases of '{repo.DisplayPath}'." ) )
            {
                var versionTagInfo = _versionTags.Get( monitor, repo );
                foreach( var tag in versionTagInfo.LastStables.Reverse() )
                {
                    if( !tag.IsRegularVersion ) continue;
                    if( await CoreBuildAsync( monitor,
                                              context,
                                              versionTagInfo,
                                              tag.Commit,
                                              tag.Version,
                                              runTest,
                                              forceRebuild: true ).ConfigureAwait( false ) != null )
                    {
                        monitor.Info( ScreenType.CKliScreenTag, $"Version '{tag.Version.ParsedText}' of '{repo.DisplayPath}' is valid." );
                        break;
                    }
                    monitor.Warn( $"Version '{tag.Version.ParsedText}' of '{repo.DisplayPath}' cannot be rebuilt." );
                    if( !warnOnly )
                    {
                        string invalidTag = $"v{tag.Version.SetBuildMetaData( null )}+invalid";
                        monitor.Info( $"Adding '{invalidTag}' on '{tag.Commit.Sha}'." );
                        repo.GitRepository.Repository.Tags.Add( invalidTag, tag.Commit );
                    }
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Rebuild the specified version in the current repository.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="version"></param>
    /// <param name="skipTests"></param>
    /// <param name="forceTests"></param>
    /// <returns></returns>
    [Description( """Rebuild the specified version in the current repository.""" )]
    [CommandPath( "maintenance rebuild version" )]
    public async Task<bool> RebuildVersionAsync( IActivityMonitor monitor,
                                                 CKliEnv context,
                                                 [Description( "The version to rebuild." )]
                                                 string version,
                                                 [Description( "Don't run tests even if they have never locally run on this commit." )]
                                                 bool skipTests = false,
                                                 [Description( "Run tests even if they have already run successfully on this commit." )]
                                                 bool forceTests = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return false;
        }
        if( !SVersion.TryParse( version, out var v ) )
        {
            monitor.Error( $"Invalid version argument: {v.ErrorMessage}." );
            return false;
        }
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null || !repo.GitRepository.CheckCleanCommit( monitor ) )
        {
            return false;
        }
        var versionTagInfo = _versionTags.GetWithoutIssue( monitor, repo );
        if( versionTagInfo == null )
        {
            return false;
        }
        if( !versionTagInfo.TryGetTagCommit( v, out var tag ) )
        {
            monitor.Error( $"Unable to find version 'v{v}'." );
            return false;
        }
        if( await CoreBuildAsync( monitor,
                                  context,
                                  versionTagInfo,
                                  tag.Commit,
                                  tag.Version,
                                  runTest,
                                  forceRebuild: true ).ConfigureAwait( false ) == null )
        {
            monitor.Error( "Build failed. See 'ckli log'." );
            return false;
        }
        monitor.Info( ScreenType.CKliScreenTag, $"Version '{tag.Version.ParsedText}' of '{repo.DisplayPath}' has been successfully rebuilt." );
        return true;
    }
}
