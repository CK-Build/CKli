using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// The "local/" and "building/" references are purely local build artifacts: they must never reach a remote.
/// <see cref="GitRepository.Push(IActivityMonitor, LibGit2Sharp.Remote, LibGit2Sharp.UsernamePasswordCredentials, System.Collections.Generic.IEnumerable{string})"/>
/// is the single low level push: filtering there is what guaranties it for "ckli push", "ckli branch push"
/// and "ckli tag push" (and for any future push).
/// </summary>
[TestFixture]
public class PushLocalRefTests
{
    // At least one test here pushes, so the write PAT for the "FILESYSTEM" is required.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    [TestCase( "local/v1.0.0", true )]
    [TestCase( "building/v1.0.0", true )]
    [TestCase( "refs/tags/local/v1.0.0", true )]
    [TestCase( "refs/tags/building/v1.0.0", true )]
    [TestCase( "refs/heads/local/xxx", true )]
    [TestCase( "refs/heads/building/xxx", true )]
    [TestCase( "", false )]
    [TestCase( "v1.0.0", false )]
    [TestCase( "refs/tags/v1.0.0", false )]
    [TestCase( "refs/heads/dev/stable", false )]
    // Only the "local/" and "building/" namespaces are concerned.
    [TestCase( "local", false )]
    [TestCase( "building", false )]
    [TestCase( "locally/v1.0.0", false )]
    [TestCase( "refs/tags/vlocal/1.0.0", false )]
    public void IsLocalOnlyRefName_test( string refName, bool isLocalOnly )
    {
        GitRepository.IsLocalOnlyRefName( refName ).ShouldBe( isLocalOnly );
    }

    [TestCase( "+refs/tags/local/v1.0.0", true )]
    [TestCase( "+refs/tags/building/v1.0.0", true )]
    [TestCase( "refs/heads/local/xxx:refs/heads/local/xxx", true )]
    // The destination is what matters.
    [TestCase( "refs/heads/xxx:refs/heads/local/xxx", true )]
    [TestCase( "refs/heads/local/xxx:refs/heads/xxx", false )]
    // A wildcard cannot be proved to exclude a "local/" or "building/" reference.
    [TestCase( "+refs/tags/*", true )]
    [TestCase( "refs/tags/*:refs/tags/*", true )]
    // Deletions are always allowed: an already pushed "local/" reference must remain removable.
    [TestCase( ":refs/tags/local/v1.0.0", false )]
    [TestCase( ":refs/heads/building/xxx", false )]
    [TestCase( "+refs/tags/v1.0.0", false )]
    [TestCase( "+refs/tags/ckli-repo", false )]
    [TestCase( "refs/heads/dev/stable:refs/heads/dev/stable", false )]
    public void IsRefusedPushRefSpec_test( string refSpec, bool isRefused )
    {
        GitRepository.IsRefusedPushRefSpec( refSpec ).ShouldBe( isRefused );
    }

    [Test]
    public void local_and_building_references_never_reach_the_remote()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "One" );
        var remoteUrl = remotes.GetUriFor( "OneRepo" );

        var timPath = context.CurrentDirectory.AppendPart( "Tim" );
        using var tim = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, remoteUrl, true ),
                                             context.Committer,
                                             timPath,
                                             timPath.LastPart ).ShouldNotBeNull();

        // 3 tags on the same commit: only the "v1.0.0" one can reach the remote.
        var tip = tim.Repository.Head.Tip;
        tim.Repository.ApplyTag( "local/v1.0.0", tip.Sha );
        tim.Repository.ApplyTag( "building/v1.0.0", tip.Sha );
        tim.Repository.ApplyTag( "v1.0.0", tip.Sha );

        tim.PushTags( TestHelper.Monitor, ["local/v1.0.0", "building/v1.0.0", "v1.0.0"] ).ShouldBeTrue();
        tim.GetRemoteTags( TestHelper.Monitor, out var remoteTags ).ShouldBeTrue();
        remoteTags.IndexedTags.ContainsKey( "refs/tags/v1.0.0" ).ShouldBeTrue();
        remoteTags.IndexedTags.ContainsKey( "refs/tags/local/v1.0.0" ).ShouldBeFalse();
        remoteTags.IndexedTags.ContainsKey( "refs/tags/building/v1.0.0" ).ShouldBeFalse();

        // A "local/" branch cannot be pushed: this is an explicit request, it is an error and the remote
        // tracking association is not even created.
        var localBranch = tim.EnsureBranch( TestHelper.Monitor, "local/xxx" ).ShouldNotBeNull();
        tim.PushBranch( TestHelper.Monitor, localBranch, autoCreateRemoteBranch: true ).ShouldBeFalse();
        tim.Repository.Branches["local/xxx"].TrackedBranch.ShouldBeNull();

        // A refused ref spec that has been deferred is dropped: it neither breaks nor is retried by the
        // next push.
        tim.DeferredPushRefSpecs.Add( "+refs/tags/local/v1.0.0" );
        var b = tim.EnsureBranch( TestHelper.Monitor, "test-1" ).ShouldNotBeNull();
        tim.Checkout( TestHelper.Monitor, b ).ShouldBeTrue();
        File.WriteAllText( tim.WorkingFolder.AppendPart( "Some.txt" ), "Hello World!" );
        tim.Commit( TestHelper.Monitor, "A commit." ).ShouldBe( CommitResult.Committed );
        // Caution with the references: the branch has been committed, use the Head.
        tim.PushBranch( TestHelper.Monitor, tim.Repository.Head, autoCreateRemoteBranch: true ).ShouldBeTrue();
        tim.DeferredPushRefSpecs.ShouldBeEmpty();

        tim.GetRemoteTags( TestHelper.Monitor, out remoteTags ).ShouldBeTrue();
        remoteTags.IndexedTags.ContainsKey( "refs/tags/local/v1.0.0" ).ShouldBeFalse( "Still not pushed." );
        tim.FetchRemoteBranches( TestHelper.Monitor, withTags: false ).ShouldBeTrue();
        tim.Repository.Branches["origin/test-1"].ShouldNotBeNull( "The regular branch has been pushed." );
        tim.Repository.Branches["origin/local/xxx"].ShouldBeNull();
    }

    [Test]
    public async Task tag_push_and_branch_push_commands_refuse_local_and_building_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        // These are rejected before the World is opened: no Stack is required here.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "tag", "push", "v1.0.0", "local/v1.0.0" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "cannot be pushed" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "tag", "push", "building/v1.0.0" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "cannot be pushed" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "push", "local/xxx" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "cannot be pushed" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "push", "building/xxx" )).ShouldBeFalse();
            logs.ShouldContain( l => l.Contains( "cannot be pushed" ) );
        }
    }
}
