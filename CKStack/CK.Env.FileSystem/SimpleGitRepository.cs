using CK.Core;
using CK.SimpleKeyVault;
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
        /// <para>Returns a SimpleGitRepository object or null on error.</para>
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="git">The Git key.</param>
        /// <param name="workingFolder">The local working folder.</param>
        /// <param name="subPath">
        /// The short path to display, relative to a well known root. It must not be empty.
        /// (this can be the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
        /// </param>
        /// <param name="branchName">
        /// The initial branch name if cloning is done and the branch that must be
        /// checked out if <paramref name="checkOutBranchName"/> is true.
        /// This branch is always created as needed (just like <see cref="GitRepository.EnsureBranch"/> does).
        /// </param>
        /// <param name="checkOutBranchName">
        /// True to always check out the <paramref name="branchName"/>
        /// even if the repository already exists.
        /// </param>
        /// <returns>The SimpleGitRepository object or null on error.</returns>
        public static SimpleGitRepository? Ensure( IActivityMonitor m,
                                                   GitRepositoryKey git,
                                                   NormalizedPath workingFolder,
                                                   NormalizedPath subPath,
                                                   string branchName,
                                                   bool checkOutBranchName )
        {
            var r = EnsureWorkingFolder( m, git, workingFolder, branchName );
            if( r == null ) return null;
            SimpleGitRepository? g = new SimpleGitRepository( git, r, workingFolder, subPath );
            return g == null ? null : CheckOutIfNeeded( m, branchName, checkOutBranchName, g );
        }

        /// <summary>
        /// Opens a working folder.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="keyStore">The key store to use.</param>
        /// <param name="workingFolder">The local working folder.</param>
        /// <param name="displayPath">
        /// The short path to display, relative to a well known root. It must not be empty.
        /// (this can be the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
        /// </param>
        /// <param name="isPublic">Whether this repository is a public or private one.</param>
        /// <param name="branchName">
        /// An optional branch name that is created and checked out if <paramref name="checkOutBranchName"/> is true.
        /// </param>
        /// <param name="checkOutBranchName">
        /// True to always check out the <paramref name="branchName"/>.
        /// </param>
        /// <returns>The SimpleGitRepository object or null on error.</returns>
        public static SimpleGitRepository? Open( IActivityMonitor monitor,
                                                 SecretKeyStore keyStore,
                                                 NormalizedPath workingFolder,
                                                 NormalizedPath displayPath,
                                                 bool isPublic,
                                                 string? branchName,
                                                 bool checkOutBranchName )
        {
            Throw.CheckArgument( !checkOutBranchName || branchName != null );
            var r = OpenWorkingFolder( monitor, workingFolder, warnOnly: false, branchName );
            if( r == null ) return null;

            var gitKey = new GitRepositoryKey( keyStore, r.Value.OriginUrl, isPublic );
            SimpleGitRepository? g = new SimpleGitRepository( gitKey, r.Value.Repository, workingFolder, displayPath );
            return g == null ? null : CheckOutIfNeeded( monitor, branchName, checkOutBranchName, g );
        }

        static SimpleGitRepository? CheckOutIfNeeded( IActivityMonitor monitor, string? branchName, bool checkOutBranchName, SimpleGitRepository g )
        {
            if( branchName != null
                && checkOutBranchName
                && branchName != g.CurrentBranchName
                && !g.Checkout( monitor, branchName ).Success )
            {
                g.Dispose();
                return null;
            }
            return g;
        }
    }
}
