using CK.Core;
using LibGit2Sharp;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Core;

public sealed partial class GitRepository
{
    /// <summary>
    /// Clones the <see cref="GitRepositoryKey.OriginUrl"/> in a local working folder
    /// that must be the 'origin' remote.
    /// <para>
    /// The remote repository can be totally empty: an initial empty commit is created in such case.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="committer">The signature to use when modifying the repository.</param>
    /// <param name="git">The Git key.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// (This is often the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
    /// </param>
    /// <returns>The GitRepository object or null on error.</returns>
    public static GitRepository? Clone( IActivityMonitor monitor,
                                        GitRepositoryKey git,
                                        Signature committer,
                                        NormalizedPath workingFolder,
                                        NormalizedPath displayPath )
    {
        var r = CloneWorkingFolder( monitor, git, workingFolder );
        return r == null ? null : new GitRepository( git, committer, r, workingFolder, displayPath );
    }

    /// <summary>
    /// Initializes a new bare repository in the specified working folder.
    /// The <see cref="GitRepositoryKey.OriginUrl"/> is <see cref="GitRepositoryKey.BareRepositoryFakeUrl"/>: a
    /// bare repository has no associated remote origin.
    /// <para>
    /// No commit can be done directly in a bare repository but the repository is initialized with an initial empty commit
    /// and its default branch and head is the provided <paramref name="branchName"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store for credentials.</param>
    /// <param name="workingFolder">The local working folder to initialize.</param>
    /// <param name="displayPath">The short path to display, relative to a well known root. It must not be empty.</param>
    /// <param name="isPublic">Whether this repository is public or private.</param>
    /// <param name="committer">The signature to use for the initial empty commit. "CKli (none)" is used by default.</param>
    /// <param name="branchName">The initial branch name. Defaults to "main".</param>
    /// <returns>The GitRepository object or null on error.</returns>
    public static GitRepository? InitBareRepository( IActivityMonitor monitor,
                                                     ISecretsStore secretsStore,
                                                     NormalizedPath workingFolder,
                                                     NormalizedPath displayPath,
                                                     bool isPublic,
                                                     Signature? committer = null,
                                                     string branchName = "main" )
    {
        var key = GitRepositoryKey.CreateBareRepositoryKey( secretsStore, isPublic );
        return InitOrphanOrBare( monitor, key, workingFolder, displayPath, branchName, committer );
    }

    /// <summary>
    /// Initializes a new bare repository in the specified working folder.
    /// The <see cref="GitRepositoryKey.OriginUrl"/> is <see cref="GitRepositoryKey.OrphanRepositoryFakeUrl"/>: an
    /// orphan repository has no associated remote origin.
    /// <para>
    /// The repository is initialized with an initial empty commit and its default branch and head is
    /// the provided <paramref name="branchName"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store for credentials.</param>
    /// <param name="workingFolder">The local working folder to initialize.</param>
    /// <param name="displayPath">The short path to display, relative to a well known root. It must not be empty.</param>
    /// <param name="isPublic">Whether this repository is public or private.</param>
    /// <param name="committer">The signature to use for the initial empty commit. "CKli (none)" is used by default.</param>
    /// <param name="branchName">The initial branch name. Defaults to "main".</param>
    /// <returns>The GitRepository object or null on error.</returns>
    public static GitRepository? InitOrphanRepository( IActivityMonitor monitor,
                                                       ISecretsStore secretsStore,
                                                       NormalizedPath workingFolder,
                                                       NormalizedPath displayPath,
                                                       bool isPublic,
                                                       Signature? committer = null,
                                                       string branchName = "main" )
    {
        var key = GitRepositoryKey.CreateOrphanRepositoryKey( secretsStore, isPublic );
        return InitOrphanOrBare( monitor, key, workingFolder, displayPath, branchName, committer );
    }

    static GitRepository? InitOrphanOrBare( IActivityMonitor monitor,
                                            GitRepositoryKey key,
                                            NormalizedPath workingFolder,
                                            NormalizedPath displayPath,
                                            string branchName,
                                            Signature? committer )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckArgument( !workingFolder.IsEmptyPath );
        Throw.CheckArgument( !displayPath.IsEmptyPath );
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );

        Throw.DebugAssert( key.IsOrphanRepository || key.IsBareRepository );

        var r = InitOrphanOrBareRepository( monitor, workingFolder, branchName, ref committer, key.IsBareRepository );
        return r != null
                ? new GitRepository( key, committer, r, workingFolder, displayPath )
                : null;
    }

    /// <summary>
    /// Initializes a local repository without remote origin. Can be a bare or a regular repository.
    /// <para>
    /// The repository is initialized with an initial empty commit and its default branch and head is
    /// the provided <paramref name="branchName"/>.
    /// </para>
    /// </summary>
    /// <param name="workingFolder">The repository (working) folder. For bare, a ".git/" subfolder is also created.</param>
    /// <param name="branchName">The default branch name.</param>
    /// <param name="committer">Committer of the initial empty commit. When null, a "CKli (none)" default signature is returned.</param>
    /// <param name="isBare">True for a bare repository, false for an orphan one.</param>
    /// <returns>The LibGit repository or null on error.</returns>
    public static Repository? InitOrphanOrBareRepository( IActivityMonitor monitor,
                                                          NormalizedPath workingFolder,
                                                          string branchName,
                                                          [NotNull] ref Signature? committer,
                                                          bool isBare )
    {
        using( monitor.OpenInfo( $"Initializing new {(isBare ? "bare" : "orphan")} repository at '{workingFolder}'." ) )
        {
            committer ??= new Signature( "CKli", "none", DateTimeOffset.Now );
            try
            {
                // Create the directory that corresponds to the working folder even for bare (they also are in a .git subfolder).
                Directory.CreateDirectory( workingFolder );
                var r = new Repository( Repository.Init( isBare ? workingFolder.AppendPart( ".git" ) : workingFolder, isBare ) );
                Throw.DebugAssert( "The repo is empty. The head is 'unborn'.", r.Head.Tip == null );
                // This simply rewrites the HEAD file.
                r.Refs.Add( "HEAD", $"refs/heads/{branchName}", allowOverwrite: true );
                // Not sure this a posteriori configuration is useful...
                r.Config.Set( "init.defaultBranch", branchName );
                // To create the branch, we need a commit.
                var tree = r.ObjectDatabase.CreateTree( new TreeDefinition() );
                var commit = r.ObjectDatabase.CreateCommit( committer, committer, "Empty initial commit.", tree, [], false );
                r.Branches.Add( branchName, commit );
                return r;
            }
            catch( Exception ex )
            {
                monitor.Error( $"Failed to initialize {(isBare ? "bare" : "orphan")} repository at '{workingFolder}'.", ex );
                return null;
            }
        }
    }

    /// <summary>
    /// Opens a working folder. The <paramref name="workingFolder"/> must exist otherwise an error is logged.
    /// <para>
    /// When <paramref name="expectedOriginUrl"/> is not null, the current "origin" must be
    /// the same (case insensitive) or it is an error otherwise casing mismatch is fixed automatically.
    /// If there is no current "origin", it is created.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="committer">The signature to use when modifying the repository.</param>
    /// <param name="secretsStore">The key store to use.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// (This is often the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
    /// </param>
    /// <param name="isPublic">Whether this repository is a public or private one.</param>
    /// <param name="expectedOriginUrl">Optional expected "origin" url.</param>
    /// <returns>The SimpleGitRepository object or null on error.</returns>
    public static GitRepository? Open( IActivityMonitor monitor,
                                       ISecretsStore secretsStore,
                                       Signature committer,
                                       NormalizedPath workingFolder,
                                       NormalizedPath displayPath,
                                       bool isPublic,
                                       Uri? expectedOriginUrl = null )
    {
        var r = OpenWorkingFolder( monitor, workingFolder, warnOnly: false, expectedOriginUrl );
        if( r == null ) return null;

        var gitKey = new GitRepositoryKey( secretsStore, r.Value.OriginUrl, isPublic );
        return new GitRepository( gitKey, committer, r.Value.Repository, workingFolder, displayPath );
    }

    /// <summary>
    /// Clones the <see cref="GitRepositoryKey.OriginUrl"/> in a local working folder
    /// that must be the 'origin' remote.
    /// <para>
    /// The remote repository can be totally empty: an initial empty commit is created in such case.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="git">The Git key.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <returns>The LibGit2Sharp Repository object or null on error.</returns>
    public static Repository? CloneWorkingFolder( IActivityMonitor monitor,
                                                  GitRepositoryKey git,
                                                  NormalizedPath workingFolder )
    {
        using( monitor.OpenInfo( $"Cloning '{workingFolder}' from '{git.OriginUrl}'." ) )
        {
            if( !git.AccessKey.GetReadCredentials( monitor, out var creds ) ) return null;
            Repository? r = null;
            try
            {
                Repository.Clone( git.OriginUrl.AbsoluteUri, workingFolder, new CloneOptions()
                {
                    FetchOptions = { CredentialsProvider = ( url, user, cred ) => creds },
                    Checkout = true
                } );
                r = new Repository( workingFolder );
                EnsureFirstCommit( monitor, r );
                return r;
            }
            catch( Exception ex )
            {
                monitor.Error( "Git clone failed. Leaving existing directory as-is.", ex );
                r?.Dispose();
                return null;
            }
        }
    }

    /// <summary>
    /// Tries to open an existing working folder. An "origin" remote must exist.
    /// <para>
    /// When <paramref name="expectedOriginUrl"/> is not null, the current "origin" must be
    /// the same (case insensitive) or it is an error otherwise casing mismatch is fixed automatically.
    /// If there is no current "origin", it is created.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="workingFolder">The local working folder (above the .git folder).</param>
    /// <param name="warnOnly">True to emit only warnings on error, false to emit errors.</param>
    /// <param name="expectedOriginUrl">Optional expected "origin" url.</param>
    /// <returns>The LibGit2Sharp repository object and its "origin" Url or null on error.</returns>
    public static (Repository Repository, Uri OriginUrl)? OpenWorkingFolder( IActivityMonitor monitor,
                                                                             NormalizedPath workingFolder,
                                                                             bool warnOnly,
                                                                             Uri? expectedOriginUrl = null )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckArgument( !workingFolder.IsEmptyPath );
        Throw.CheckArgument( expectedOriginUrl == null || expectedOriginUrl.IsAbsoluteUri );
        try
        {
            var errorLevel = warnOnly ? LogLevel.Warn : LogLevel.Error;
            var gitFolderPath = workingFolder.AppendPart( ".git" );
            if( !Directory.Exists( gitFolderPath ) )
            {
                monitor.Log( errorLevel, $"The folder '{gitFolderPath}' does not exist." );
                return null;
            }
            if( !Repository.IsValid( gitFolderPath ) )
            {
                monitor.Log( errorLevel, $"Git folder '{gitFolderPath}' exists but is not a valid Repository. This must be fixed manually." );
                return null;
            }
            Uri? originUrl = null;
            var r = new Repository( workingFolder );
            var origin = r.Network.Remotes.FirstOrDefault( rem => rem.Name == "origin" );
            if( origin == null )
            {
                if( expectedOriginUrl == null )
                {
                    monitor.Log( errorLevel, $"""
                                          Existing '{workingFolder}' must have an 'origin' remote. Remotes are: '{r.Network.Remotes.Select( r => r.Name ).Concatenate( "', '" )}'.
                                          This must be fixed manually.
                                          """ );
                    r.Dispose();
                    return null;
                }
                origin = r.Network.Remotes.Add( "origin", expectedOriginUrl.ToString() );
                originUrl = expectedOriginUrl;
            }
            else
            {
                if( !Uri.TryCreate( origin.Url, UriKind.Absolute, out originUrl ) )
                {
                    monitor.Log( errorLevel, $"""
                                          Existing '{workingFolder}' has its 'origin' that is not a valid absolute Uri: '{origin.Url}'.
                                          This must be fixed manually.
                                          """ );
                    r.Dispose();
                    return null;
                }
                if( expectedOriginUrl != null )
                {
                    if( !GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer.Equals( expectedOriginUrl, originUrl ) )
                    {
                        monitor.Log( errorLevel, $"""
                                          Existing '{workingFolder}' has its 'origin' set to '{origin.Url}' but the expected origin is '{expectedOriginUrl}'.
                                          This must be fixed manually.
                                          """ );
                        r.Dispose();
                        return null;
                    }
                    if( !StringComparer.Ordinal.Equals( expectedOriginUrl.ToString(), origin.Url.ToString() ) )
                    {
                        monitor.Trace( $"Fixed case for origin url of '{workingFolder}' from '{origin.Url}' to '{expectedOriginUrl}'." );
                        r.Network.Remotes.Update( "origin", u => u.Url = expectedOriginUrl.ToString() );
                        originUrl = expectedOriginUrl;
                    }
                }
            }
            EnsureFirstCommit( monitor, r );
            return (r, originUrl);
        }
        catch( Exception ex )
        {
            monitor.Fatal( $"Failed to open Git '{workingFolder}'.", ex );
            return null;
        }
    }

    static void EnsureFirstCommit( IActivityMonitor m, Repository r )
    {
        if( !r.Commits.Any() )
        {
            m.Info( $"Uninitialized repository: automatically creating an initial commit." );
            var date = DateTimeOffset.Now;
            Signature author = r.Config.BuildSignature( date );
            var committer = new Signature( "CKli", "none", date );
            r.Commit( "Initial commit automatically created.", author, committer, new CommitOptions { AllowEmptyCommit = true } );
        }
    }


}
