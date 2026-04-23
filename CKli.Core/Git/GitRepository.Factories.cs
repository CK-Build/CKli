using CK.Core;
using LibGit2Sharp;
using System;
using System.IO;
using System.Linq;
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
    /// Initializes a new Git repository in the specified working folder.
    /// <para>
    /// Unlike <see cref="Clone"/>, this creates a new repository locally rather than cloning from a remote.
    /// An "origin" remote is set to the specified URL (can be a file:// URL for local-only stacks).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store for credentials.</param>
    /// <param name="committer">The signature to use when modifying the repository.</param>
    /// <param name="workingFolder">The local working folder to initialize.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// </param>
    /// <param name="isPublic">Whether this repository is public or private.</param>
    /// <param name="originUrl">The URL to set as the "origin" remote. Can be a file:// URL for local-only stacks.</param>
    /// <param name="branchName">The initial branch name. Defaults to "main".</param>
    /// <returns>The GitRepository object or null on error.</returns>
    public static GitRepository? Init( IActivityMonitor monitor,
                                       ISecretsStore secretsStore,
                                       Signature committer,
                                       NormalizedPath workingFolder,
                                       NormalizedPath displayPath,
                                       bool isPublic,
                                       Uri originUrl,
                                       string branchName = "main" )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckArgument( !workingFolder.IsEmptyPath );
        Throw.CheckArgument( !displayPath.IsEmptyPath );
        Throw.CheckNotNullArgument( originUrl );
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );

        using( monitor.OpenInfo( $"Initializing new repository at '{workingFolder}'." ) )
        {
            try
            {
                // Create the directory
                Directory.CreateDirectory( workingFolder );

                // Initialize the repository with the specified initial branch
                Repository.Init( workingFolder, isBare: false );

                var r = new Repository( workingFolder );

                // Set the default branch to the specified branchName (instead of "master")
                // For an unborn branch, we need to set HEAD as a symbolic reference
                r.Refs.Add( "HEAD", $"refs/heads/{branchName}", allowOverwrite: true );

                // Add origin remote
                r.Network.Remotes.Add( "origin", originUrl.AbsoluteUri );
                monitor.Info( $"Added 'origin' remote: {originUrl}" );

                var gitKey = new GitRepositoryKey( secretsStore, originUrl, isPublic );

                return new GitRepository( gitKey, committer, r, workingFolder, displayPath );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Failed to initialize repository at '{workingFolder}'.", ex );
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
