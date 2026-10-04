using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Linq;

namespace CKli.HotZone.Plugin;

// The merge assistant of the World: the package versions that conflict when a branch is merged (by "ckli pull" and
// "ckli push") are resolved the way a build of the branch would update them, exactly like "ckli branch sync" does,
// and a branch name given to "--branch" is the branch the person works on: its "dev/" branch when it exists.
public sealed partial class HotZonePlugin : IMergeAssistant
{
    /// <summary>
    /// Registers this plugin as the <see cref="World.MergeAssistant"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>Always true.</returns>
    protected override bool Initialize( IActivityMonitor monitor )
    {
        World.SetMergeAssistant( this );
        return true;
    }

    IMergeSession IMergeAssistant.OpenSession( IActivityMonitor monitor ) => new MergeSession( this, CreateVersionResolverProvider() );

    sealed class MergeSession : IMergeSession
    {
        readonly HotZonePlugin _plugin;
        readonly Func<IActivityMonitor, BranchName, IPackageVersionResolver?> _resolverProvider;

        public MergeSession( HotZonePlugin plugin, Func<IActivityMonitor, BranchName, IPackageVersionResolver?> resolverProvider )
        {
            _plugin = plugin;
            _resolverProvider = resolverProvider;
        }

        public Branch? GetWorkingBranch( IActivityMonitor monitor, Repo repo, string branchName )
        {
            var git = repo.GitRepository;
            var name = _plugin._branchModel.BranchNamespace.Find( branchName );
            if( name == null )
            {
                // Not a branch of the branch model: the name is taken literally.
                return git.GetBranch( monitor, branchName, CK.Core.LogLevel.None );
            }
            return git.GetBranch( monitor, name.DevName, CK.Core.LogLevel.None )
                   ?? git.GetBranch( monitor, name.Name, CK.Core.LogLevel.None );
        }

        public MergeSidesAligner? GetAligner( IActivityMonitor monitor, Repo repo, Branch target )
        {
            var targetName = target.FriendlyName;
            var name = _plugin._branchModel.BranchNamespace.ByName.Values
                              .FirstOrDefault( b => b.Name == targetName || b.DevName == targetName );
            // A branch that is not in the branch model (a "fix/" one for instance) has no Hot Graph.
            if( name == null ) return null;
            return PackageVersionMerge.CreateAligner( repo.GitRepository,
                                                      () => _resolverProvider( monitor, name ),
                                                      aligned => monitor.Info( $"""
                                                          Merging into '{targetName}' in '{repo.DisplayPath}' aligned {aligned.Count} package version(s):
                                                          {aligned.Select( a => $"{a.PackageId}: {a.Ours} / {a.Theirs} => {a.Resolved}" ).Concatenate( Environment.NewLine )}
                                                          """ ) );
        }

        public void Dispose()
        {
        }
    }
}
