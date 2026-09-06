using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// A Stack repository has a single branch: "main" by convention (this is what <see cref="StackRepository.CreateAsync"/>
/// creates). A Stack repository that predates this convention has a "master" one: it must still be usable.
/// </summary>
[TestFixture]
public class StackBranchTests
{
    // Pushing the Stack requires the write PAT for the "FILESYSTEM".
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [Test]
    public async Task a_Stack_without_a_main_branch_works_on_its_default_branch_Async()
    {
        var root = TestEnv.EnsureCleanFolder();
        // The CKt-Stack remote has no "main" branch: "master" is its default (and only) branch.
        var remotes = TestEnv.OpenRemotes( "CKt" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, root, "clone", remotes.StackUri )).ShouldBeTrue();
        var inStack = root.ChangeDirectory( "CKt" );
        using( var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                          .ShouldNotBeNull() )
        {
            var git = stack.GitRepository;
            git.CurrentBranchName.ShouldBe( "master" );
            git.Repository.Branches["main"].ShouldBeNull( "No purely local 'main' branch has been created." );
            git.Repository.Head.TrackedBranch.ShouldNotBeNull( "The head tracks the remote: it can be pushed." );
        }

        // Before the fix, the Stack was on a purely local "main" and this failed with
        // "Branch 'main' has no tracked branch.". This is what "ckli push" does.
        File.WriteAllText( inStack.CurrentDirectory.Combine( ".PublicStack/Some.txt" ), "Hello!" );
        using( var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, inStack, out _, skipPullStack: true )
                                          .ShouldNotBeNull() )
        {
            stack.PushChanges( TestHelper.Monitor ).ShouldBeTrue();
        }

        // The remote has it: another clone sees it.
        StackRepository.ClearRegistry( TestHelper.Monitor ).ShouldBeTrue();
        var other = root.ChangeDirectory( "Other" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, other, "clone", remotes.StackUri )).ShouldBeTrue();
        File.Exists( other.CurrentDirectory.Combine( "CKt/.PublicStack/Some.txt" ) ).ShouldBeTrue();
    }
}
