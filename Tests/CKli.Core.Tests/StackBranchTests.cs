using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// A Stack repository has a single branch and it is <see cref="StackRepository.BranchName"/>. This is an
/// invariant: a Stack repository that has another one is refused rather than worked on, because a Stack read
/// from one branch and named on another cannot be made coherent (see <see cref="StackRepository.BranchName"/>).
/// </summary>
[TestFixture]
public class StackBranchTests
{
    [Test]
    public async Task the_Stack_repository_is_on_the_stack_branch_and_tracks_its_remote_Async()
    {
        var root = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, root, "clone", remotes.StackUri )).ShouldBeTrue();

        var inStack = root.ChangeDirectory( "CKt" );
        using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                         .ShouldNotBeNull();
        var git = stack.GitRepository;
        git.CurrentBranchName.ShouldBe( StackRepository.BranchName );
        git.Repository.Head.TrackedBranch.ShouldNotBeNull( "The head tracks the remote: it can be pushed." );
    }

    [Test]
    public async Task a_Stack_repository_without_the_stack_branch_is_refused_Async()
    {
        var root = TestEnv.EnsureCleanFolder();
        // OpenRemotes re-extracts the bare repository on every call: renaming its branch cannot leak
        // into another test.
        var remotes = TestEnv.OpenRemotes( "CKt" );
        RenameStackBranch( remotes.StackUri, StackRepository.BranchName, "master" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, root, "clone", remotes.StackUri )).ShouldBeFalse();
            logs.ShouldContain( t => t.Contains( $"has no '{StackRepository.BranchName}' branch" )
                                     && t.Contains( "it is on 'master'" ) );
        }
    }

    /// <summary>
    /// Renames a branch of a bare repository and moves its HEAD onto it: this is what a Stack repository that
    /// predates the <see cref="StackRepository.BranchName"/> convention looks like.
    /// </summary>
    static void RenameStackBranch( System.Uri bareStackUri, string from, string to )
    {
        using var repo = new Repository( Repository.Discover( bareStackUri.LocalPath ) );
        var renamed = repo.Refs.Rename( $"refs/heads/{from}", $"refs/heads/{to}" );
        // The Reference overload retargets the symbolic HEAD. The string one would make it direct:
        // HEAD would be detached and the clone would land on no branch at all.
        repo.Refs.UpdateTarget( repo.Refs.Head, renamed );
        repo.Refs.Head.TargetIdentifier.ShouldBe( $"refs/heads/{to}" );
    }
}
