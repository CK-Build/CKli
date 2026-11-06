using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// On Windows, libgit2 fails on a path longer than 260 characters unless "core.longpaths" is true. A branch switch
/// leaves the files that the previous branch ignored (a Yarn install, nested test repositories) as untracked files:
/// a long path among them used to break the status of the repository, and with it every command.
/// </summary>
public partial class GitRepositoryTests
{
    [Test]
    public void long_paths_are_enabled_by_the_repository()
    {
        if( !OperatingSystem.IsWindows() ) Assert.Ignore( "Windows only." );
        var context = TestEnv.EnsureCleanFolder();
        using var r = CreateRepositoryWithCommit( context );
        r.Repository.Config.Get<bool>( "core.longpaths", ConfigurationLevel.Local ).ShouldNotBeNull().Value.ShouldBeTrue();

        WriteLongPathFile( context.CurrentDirectory.AppendPart( "Untracked" ) );
        var status = r.GetSimpleStatusInfo();
        status.StatusError.ShouldBeNull();
        status.IsDirty.ShouldBeTrue();
    }

    /// <summary>
    /// An explicit "core.longpaths = false" of the repository is kept: the status cannot be read, this is an error of
    /// the repository (it is dirty: its state is unknown), not an exception for the command.
    /// </summary>
    [Test]
    public void a_status_that_cannot_be_read_is_an_error_of_the_repository()
    {
        if( !OperatingSystem.IsWindows() ) Assert.Ignore( "Windows only." );
        var context = TestEnv.EnsureCleanFolder();
        using( var init = CreateRepositoryWithCommit( context ) )
        {
            init.Repository.Config.Set( "core.longpaths", false, ConfigurationLevel.Local );
        }
        using var r = OpenRepository( context );
        r.Repository.Config.Get<bool>( "core.longpaths", ConfigurationLevel.Local ).ShouldNotBeNull().Value.ShouldBeFalse();

        WriteLongPathFile( context.CurrentDirectory.AppendPart( "Untracked" ) );
        var status = r.GetSimpleStatusInfo();
        status.StatusError.ShouldNotBeNull();
        status.IsDirty.ShouldBeTrue();
        status.CurrentBranchName.ShouldBe( "main" );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            r.CheckCleanCommit( TestHelper.Monitor ).ShouldBeFalse();
            logs.ShouldContain( t => t.StartsWith( $"The status of repository '{r.DisplayPath}' cannot be read (main): " ) );
        }
    }

    /// <summary>
    /// The files that a branch ignores are untracked on a branch that doesn't ignore them: the checkout deletes them.
    /// When it can't, the checkout is done but a warning says that the repository is dirty and what to do.
    /// </summary>
    [TestCase( true )]
    [TestCase( false )]
    public void checkout_deletes_the_files_left_by_the_previous_branch( bool longPaths )
    {
        if( !OperatingSystem.IsWindows() ) Assert.Ignore( "Windows only." );
        var context = TestEnv.EnsureCleanFolder();
        var root = context.CurrentDirectory;
        using( var init = CreateRepositoryWithCommit( context ) )
        {
            // "main" ignores "Generated/", "other" doesn't.
            init.Repository.CreateBranch( "other" );
            File.WriteAllText( root.AppendPart( ".gitignore" ), "Generated/" );
            init.Commit( TestHelper.Monitor, "Ignoring Generated/." ).ShouldBe( CommitResult.Committed );
            if( !longPaths ) init.Repository.Config.Set( "core.longpaths", false, ConfigurationLevel.Local );
        }
        using var r = OpenRepository( context );
        WriteLongPathFile( root.AppendPart( "Generated" ) );
        r.GetSimpleStatusInfo().IsDirty.ShouldBeFalse( "Ignored on main." );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            r.Checkout( TestHelper.Monitor, r.Repository.Branches["other"] ).ShouldBeTrue();
            r.Repository.Head.FriendlyName.ShouldBe( "other" );
            if( longPaths )
            {
                Directory.Exists( root.AppendPart( "Generated" ) ).ShouldBeFalse();
                r.GetSimpleStatusInfo().IsDirty.ShouldBeFalse();
                logs.ShouldNotContain( t => t.StartsWith( "Checked out " ) );
            }
            else
            {
                Directory.Exists( root.AppendPart( "Generated" ) ).ShouldBeTrue();
                r.GetSimpleStatusInfo().IsDirty.ShouldBeTrue();
                logs.ShouldContain( t => t.StartsWith( $"Checked out '{r.DisplayPath}/other' but the files left by the previous branch could not all be deleted: the repository is dirty." )
                                         && t.Contains( "'git clean -fd'" ) );
            }
        }
    }

    static GitRepository CreateRepositoryWithCommit( CKliEnv context )
    {
        var r = GitRepository.InitOrphanRepository( TestHelper.Monitor,
                                                    context.SecretsStore,
                                                    context.CurrentDirectory,
                                                    context.CurrentDirectory.LastPart,
                                                    isPublic: true );
        r.ShouldNotBeNull();
        File.WriteAllText( context.CurrentDirectory.AppendPart( "SomeFile.txt" ), "Some content." );
        r.Commit( TestHelper.Monitor, "First commit." ).ShouldBe( CommitResult.Committed );
        return r;
    }

    // A new GitRepository: libgit2 caches the configuration values of a repository once read.
    // An orphan repository has no origin: Open requires one (it is never used here).
    static GitRepository OpenRepository( CKliEnv context )
    {
        var r = GitRepository.Open( TestHelper.Monitor,
                                    context.SecretsStore,
                                    new Signature( "CKli", "none", DateTimeOffset.Now ),
                                    context.CurrentDirectory,
                                    context.CurrentDirectory.LastPart,
                                    isPublic: true,
                                    expectedOriginUrl: new Uri( "file:///C:/Nowhere/Origin" ) );
        return r.ShouldNotBeNull();
    }

    // A file whose full path is longer than 260 characters.
    static void WriteLongPathFile( NormalizedPath folder )
    {
        var segment = new string( 'x', 50 );
        var path = folder;
        while( path.Path.Length < 300 ) path = path.AppendPart( segment );
        Directory.CreateDirectory( path );
        File.WriteAllText( path.AppendPart( "file.txt" ), "Deep." );
    }
}
