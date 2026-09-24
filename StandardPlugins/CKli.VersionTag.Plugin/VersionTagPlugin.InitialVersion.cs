using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Linq;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
    /// <summary>
    /// Creates the "+fake" tag that gives a repository its initial version on its root branch (this is idempotent:
    /// nothing is done when the tag already exists).
    /// <para>
    /// A "+fake" must not share a commit with another version. When the tip of the root branch already bears a
    /// version tag - even one that this World ignores, below its InfVersion - an empty commit is added on the root
    /// branch first. This is the case of the default World right after a Long Term Support world has been created:
    /// the tip carries the last published version, which now belongs to the LTS world.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="root">The local root branch.</param>
    /// <param name="initialFakeVersion">The "vX.Y.Z+fake" tag name.</param>
    /// <returns>True on success, false on error.</returns>
    public bool CreateInitialFakeVersion( IActivityMonitor monitor, Repo repo, Branch root, string initialFakeVersion )
    {
        Throw.CheckArgument( initialFakeVersion.EndsWith( "+fake", StringComparison.Ordinal ) );
        var git = repo.GitRepository;
        if( git.Repository.Tags[initialFakeVersion] != null )
        {
            monitor.Info( $"Tag '{initialFakeVersion}' already exists in '{repo.DisplayPath}'." );
            return true;
        }
        try
        {
            var tip = root.Tip;
            var bearsVersion = git.Repository.Tags.Any( t => t.PeeledTarget.Sha == tip.Sha
                                                             && SVersion.ParseNoThrow( t.FriendlyName ).IsValid );
            if( bearsVersion )
            {
                // The tree is the same: whether the root branch is checked out or not, the working folder is unchanged.
                var empty = git.Repository.ObjectDatabase.CreateCommit( git.Author,
                                                                        git.Committer,
                                                                        $"Starting '{initialFakeVersion}'.",
                                                                        tip.Tree,
                                                                        [tip],
                                                                        prettifyMessage: false );
                git.Repository.Refs.UpdateTarget( root.Reference, empty.Id, $"Starting '{initialFakeVersion}'." );
                tip = empty;
                monitor.Info( $"Created an empty commit on '{root.FriendlyName}' of '{repo.DisplayPath}': its tip already bears a version." );
            }
            monitor.Info( $"Creating '{initialFakeVersion}' on '{root.FriendlyName}' of '{repo.DisplayPath}'." );
            git.Repository.Tags.Add( initialFakeVersion, tip );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While creating '{initialFakeVersion}' in '{repo.DisplayPath}'.", ex );
            return false;
        }
    }
}
