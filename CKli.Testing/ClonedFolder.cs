
using CK.Core;
using CK.Testing;
using CKli.Core;
using LibGit2Sharp;
using Shouldly;
using System;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CKli;

/// <summary>
/// Encapsulates a specific test folder in "Cloned/".
/// Obtained with <see cref="CKliTestHelperExtensions.InitializeClonedFolder(IMonitorTestHelper, string?, bool)"/>.
/// </summary>
public sealed class ClonedFolder
{
    readonly NormalizedPath _path;

    internal ClonedFolder( NormalizedPath path )
    {
        _path = path;
    }

    /// <summary>
    /// Gets the cloned folder path.
    /// </summary>
    public NormalizedPath Path => _path;

    /// <summary>
    /// Initializes a new bare repository with a <see cref="GitRepositoryKey.BareRepositoryFakeUrl"/> remote origin url.
    /// <para>
    /// Initial branch is "main" and an initial empty commit exists.
    /// </para>
    /// </summary>
    /// <param name="repositoryName">The repository name (the folder name).</param>
    /// <param name="repositoryUrl">The remote url from which this bare repository can be cloned.</param>
    /// <returns>The repository.</returns>
    public GitRepository CreateBareRepository( out Uri repositoryUrl, string repositoryName = "Remote" ) => CreateBareRepository( _path, repositoryName, out repositoryUrl );

    /// <summary>
    /// Clones a repository from its remote origin url.
    /// </summary>
    /// <param name="name">The repository name (the folder name).</param>
    /// <param name="remoteUrl">The remote origin url.</param>
    /// <returns>The repository.</returns>
    public GitRepository CloneGitRepository( string name, Uri remoteUrl ) => CloneGitRepository( _path, name, remoteUrl );

    /// <summary>
    /// Initializes a new orphan repository with a <see cref="GitRepositoryKey.OrphanRepositoryFakeUrl"/> remote origin url.
    /// <para>
    /// Initial branch is "main" and an initial empty commit exists.
    /// </para>
    /// </summary>
    /// <param name="repositoryName">The repository name (the folder name).</param>
    /// <returns>The repository.</returns>
    public GitRepository CreateOrphanGitRepository( string repositoryName ) => CreateOrphanGitRepository( _path, repositoryName );

    /// <summary>
    /// Initializes a new bare repository with a <see cref="GitRepositoryKey.BareRepositoryFakeUrl"/> remote origin url.
    /// <para>
    /// Initial branch is "main" and an initial empty commit exists.
    /// </para>
    /// </summary>
    /// <param name="folder">The folder in which the <paramref name="repositoryName"/> repository must be created.</param>
    /// <param name="repositoryName">The repository name (the folder name).</param>
    /// <param name="repositoryUrl">The remote url from which this bare repository can be cloned.</param>
    /// <returns>The repository.</returns>
    public static GitRepository CreateBareRepository( NormalizedPath folder, string repositoryName, out Uri repositoryUrl )
    {
        var gitPath = folder.AppendPart( repositoryName );
        Directory.Exists( gitPath ).ShouldBeFalse();

        Directory.CreateDirectory( gitPath );
        var committer = CKliRootEnv.DefaultCKliEnv.Committer;

        var git = GitRepository.InitBareRepository( TestHelper.Monitor,
                                                    CKliRootEnv.SecretsStore,
                                                    gitPath,
                                                    gitPath.LastPart,
                                                    isPublic: true,
                                                    committer ).ShouldNotBeNull();
        repositoryUrl = new Uri( gitPath );
        return git;
    }

    /// <summary>
    /// Clones a repository from its remote origin url.
    /// </summary>
    /// <param name="folder">The folder in which the <paramref name="repositoryName"/> repository must be created.</param>
    /// <param name="repositoryName">The repository name (the folder name).</param>
    /// <param name="remoteUrl">The remote origin url.</param>
    /// <returns>The repository.</returns>
    public static GitRepository CloneGitRepository( NormalizedPath folder, string repositoryName, Uri remoteUrl )
    {
        var gitPath = folder.AppendPart( repositoryName );
        Directory.Exists( gitPath ).ShouldBeFalse();

        Directory.CreateDirectory( gitPath );
        var key = new GitRepositoryKey( CKliRootEnv.SecretsStore, remoteUrl, isPublic: true );
        return GitRepository.Clone( TestHelper.Monitor, key, CKliRootEnv.DefaultCKliEnv.Committer, gitPath, repositoryName ).ShouldNotBeNull();
    }

    /// <summary>
    /// Initializes a new orphan repository with a <see cref="GitRepositoryKey.OrphanRepositoryFakeUrl"/> remote origin url.
    /// <para>
    /// Initial branch is "main" and an initial empty commit exists.
    /// </para>
    /// </summary>
    /// <param name="folder">The folder in which the <paramref name="repositoryName"/> repository must be created.</param>
    /// <param name="repositoryName">The repository name (the folder name).</param>
    /// <returns>The repository.</returns>
    public static GitRepository CreateOrphanGitRepository( NormalizedPath folder, string repositoryName )
    {
        var gitPath = folder.AppendPart( repositoryName );
        Directory.Exists( gitPath ).ShouldBeFalse();

        Directory.CreateDirectory( gitPath );
        var git = GitRepository.InitOrphanRepository( TestHelper.Monitor,
                                                      CKliRootEnv.SecretsStore,
                                                      gitPath,
                                                      gitPath.LastPart,
                                                      isPublic: true,
                                                      CKliRootEnv.DefaultCKliEnv.Committer ).ShouldNotBeNull();
        return git;
    }


}
