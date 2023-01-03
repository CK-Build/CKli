using CK.Core;

using LibGit2Sharp;
using System;

namespace CK.Env
{
    /// <summary>
    /// Autonomous <see cref="GitRepositoryBase"/> implementation that must be disposed once done.
    /// </summary>
    public class SimpleGitRepository : GitRepositoryBase
    {
        SimpleGitRepository( GitRepositoryKey repositoryKey, Repository libRepository, NormalizedPath fullPath, NormalizedPath subPath )
            : base( repositoryKey, libRepository, fullPath, subPath )
        {
        }

        /// <summary>
        /// Checks out a working folder if needed or checks that an existing one is
        /// bound to the <see cref="GitRepositoryKey.OriginUrl"/> 'origin' remote, ensuring
        /// that the specified branch name exists (and optionally checked out).
        /// <para>Returns the LibGit2Sharp repository object or null on error.</para>
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="git">The Git key.</param>
        /// <param name="workingFolder">The local working folder.</param>
        /// <param name="subPath">
        /// The short path to display, relative to a well known root. It must not be empty.
        /// (this can be the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
        /// </param>
        /// <param name="ensureHooks">True to create the standard hooks.</param>
        /// <param name="branchName">
        /// The initial branch name if cloning is done and the branch that must be
        /// checked out if <paramref name="checkOutBranchName"/> is true.
        /// This branch is always created as needed (just like <see cref="GitRepository.EnsureBranch"/> does).
        /// </param>
        /// <param name="checkOutBranchName">
        /// True to always check out the <paramref name="branchName"/>
        /// even if the repository already exists.
        /// </param>
        /// <returns>The LibGit2Sharp repository object or null on error.</returns>
        public static SimpleGitRepository? Create( IActivityMonitor m,
                                                   GitRepositoryKey git,
                                                   NormalizedPath workingFolder,
                                                   NormalizedPath subPath,
                                                   bool ensureHooks,
                                                   string branchName,
                                                   bool checkOutBranchName )
        {
            var r = EnsureWorkingFolder( m, git, workingFolder, ensureHooks, branchName );
            if( r == null ) return null;
            SimpleGitRepository? g = new SimpleGitRepository( git, r, workingFolder, subPath );
            if( checkOutBranchName
                && branchName != g.CurrentBranchName
                && !g.Checkout( m, branchName ).Success )
            {
                g.Dispose();
                g = null;
            }
            return g;
        }
    }
}
