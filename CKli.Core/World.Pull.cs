using CK.Core;
using LibGit2Sharp;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core;

public sealed partial class World
{
    /// <summary>
    /// Pulls the Stack repository while this World is running: this is for a command that opened the World without
    /// pulling the Stack and must now work on its remote state (a publication, under its lock).
    /// <para>
    /// The incoming commits must not change this World's definition file nor its plugins: the running World has
    /// loaded them and cannot follow. This is an error that asks to pull and to run the command again.
    /// </para>
    /// <para>
    /// A conflicting merge is an error that leaves nothing behind. When a merge commit is created, the
    /// <see cref="WorldEvents.StackMerged"/> event is raised.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public async Task<bool> PullStackAsync( IActivityMonitor monitor )
    {
        var git = _stackRepository.GitRepository;
        if( git.Repository.Head.TrackedBranch == null )
        {
            // FetchMergeHead warns and does nothing.
            return git.FetchMergeHead( monitor );
        }
        if( !git.FetchRemoteBranches( monitor, withTags: false, cancellation: _scopeAlive ) )
        {
            return false;
        }
        var local = git.Repository.Head.Tip;
        var remote = git.Repository.Head.TrackedBranch?.Tip;
        if( local != null && remote != null )
        {
            var mergeBase = git.Repository.ObjectDatabase.FindMergeBase( local, remote );
            if( mergeBase?.Sha != remote.Sha )
            {
                var locked = GetLockedPaths( git.Repository.Diff.Compare<TreeChanges>( mergeBase?.Tree, remote.Tree ) );
                if( locked.Count > 0 )
                {
                    monitor.Error( $"""
                        The remote Stack changed what this World has loaded: '{locked.Concatenate( "', '" )}'.
                        Run 'ckli pull' and then the command again.
                        """ );
                    return false;
                }
            }
        }
        if( !git.FetchMergeHead( monitor, out var mergedPaths ) )
        {
            return false;
        }
        if( mergedPaths.Length > 0 && _events._stackMergedEventSender.HasHandlers )
        {
            var e = new StackMergedEventArgs( monitor, _stackRepository.Context, this, mergedPaths );
            return await _events._stackMergedEventSender.SafeRaiseAsync( monitor, e ).ConfigureAwait( false ) && e.Success;
        }
        return true;

        List<string> GetLockedPaths( TreeChanges incoming )
        {
            var result = new List<string>();
            foreach( var c in incoming )
            {
                foreach( var p in c.Status == ChangeKind.Renamed ? [c.OldPath, c.Path] : new[] { c.Path } )
                {
                    var path = _stackRepository.StackWorkingFolder.Combine( p );
                    if( path == _name.XmlDescriptionFilePath
                        || (_pluginMachinery != null && path.StartsWith( _pluginMachinery.Root )) )
                    {
                        result.Add( p );
                    }
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Pulls the <paramref name="repos"/>: this is what "ckli pull" does on the repositories (fetch and merge of the
    /// branches that track a remote one, then the tags). Any merge conflict is an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repos">The repositories to pull.</param>
    /// <param name="withTags">True to let the remote tags replace the local ones.</param>
    /// <param name="maxDop">Maximal degree of parallelism. 0 (or less) is unbounded.</param>
    /// <param name="continueOnError">True to pull every repository regardless of the errors.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> PullAsync( IActivityMonitor monitor,
                                 IReadOnlyList<Repo> repos,
                                 bool withTags = false,
                                 int maxDop = 0,
                                 bool continueOnError = false )
    {
        return CKli.CKliPull.DoPullAsync( monitor, continueOnError, repos, withTags, maxDop, _scopeAlive );
    }
}
