using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public partial class PullPushTests
{
    [SetUp]
    public void Setup()
    {
        // Because we are pushing here, we need the Write PAT for the "FILESYSTEM"
        // That is useless (credentials are not used on local file system) but it's
        // good to not make an exception for this case.
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets set FILESYSTEM_GIT_WRITE_PAT "don't care" --id CKli-Test""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }

    [Test]
    public async Task fetch_merge_push_and_pull_Async()
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
        var timFilePath = timPath.AppendPart( "Some.txt" );

        var bobPath = context.CurrentDirectory.AppendPart( "Bob" );
        using var bob = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, remoteUrl, true ),
                                             context.Committer,
                                             bobPath,
                                             bobPath.LastPart ).ShouldNotBeNull();
        var bobFilePath = bobPath.AppendPart( "Some.txt" );

        tim.CurrentBranchName.ShouldBe( "master" );
        bob.CurrentBranchName.ShouldBe( "master" );

        // Bob fetches a branch (unknown to him) that doesn't exist: it is not an error but the resulting Branch is null.
        {
            bob.FetchRemoteBranch( TestHelper.Monitor, "kexistepas", withTags: false, out var noWay ).ShouldBeTrue();
            noWay.ShouldBeNull();
        }

        // Tim creates branch "test-1" with a commit and pushes it.
        CreateBranchAndPush( tim, 1 );

        // Bob fetches the "test-1" branch (unknown to him).
        {
            bob.FetchRemoteBranch( TestHelper.Monitor, "test-1", withTags: false, out var bobBranchTest1 ).ShouldBeTrue();
            bobBranchTest1.ShouldNotBeNull( "The local branch is created..." )
                .TrackedBranch.ShouldNotBeNull( "...and it is a tracking branch." );
            // The branch's tip is the same as its tracked one.
            bobBranchTest1.Tip.Sha.ShouldBe( bobBranchTest1.TrackedBranch.Tip.Sha );
            bobBranchTest1.Tip.Tree["Some1.txt"].ShouldNotBeNull();
        }

        // Tim creates branch "test-2" with a commit and pushes it.
        CreateBranchAndPush( tim, 2 );

        // Fetch-Merge a branch that is not checked out.
        //
        // Bob also creates a local "test-2" branch with a commit, checks out "master" and
        // fetch and merges "test-2".
        {
            var bobMaster = bob.Repository.Head;
            bobMaster.FriendlyName.ShouldBe( "master" );

            Branch initial = bob.Repository.Branches.Add( "test-2", bobMaster.Tip );
            bob.Checkout( TestHelper.Monitor, initial ).ShouldBeTrue();
            File.WriteAllText( bob.WorkingFolder.AppendPart( "SomeOther.txt" ), "Hop!" );
            bob.Commit( TestHelper.Monitor, "Bob's commit on 'test-2'." ).ShouldBe( CommitResult.Commited );

            // Caution with the references!
            initial.Tip.Tree["SomeOther.txt"].ShouldBeNull();
            // The initial branch is the Head... but after the commit!
            initial = bob.Repository.Head;
            initial.Tip.Tree["SomeOther.txt"].ShouldNotBeNull();
            initial.FriendlyName.ShouldBe( "test-2" );
            initial.TrackedBranch.ShouldBeNull( "Currently purely local." );

            // Back to "master".
            bob.Checkout( TestHelper.Monitor, bobMaster ).ShouldBeTrue();

            // The remote branch is fetched: "test-2" is now tracking the 'origin' remote.
            // The "test-2" local branch has not moved, it doesn't contain the "Some2.txt" file.
            bob.FetchRemoteBranch( TestHelper.Monitor, "test-2", withTags: false, out var afterFetch ).ShouldBeTrue();
            afterFetch.ShouldNotBeNull();
            afterFetch.Tip.Sha.ShouldBe( initial.Tip.Sha );
            afterFetch.TrackedBranch.FriendlyName.ShouldBe( "origin/test-2" );
            afterFetch.Tip.Tree["Some2.txt"].ShouldBeNull();
            afterFetch.TrackedBranch.Tip.Tree["Some2.txt"].ShouldNotBeNull( "The remote branch has the 'Some2.txt' file." );

            // Now merging the remote into the local.
            var afterMerge = afterFetch;
            bob.MergeTrackedBranch( TestHelper.Monitor, ref afterMerge ).ShouldBeTrue();
            // The new afterFetch has a new Tip: the merge commit.
            afterMerge.Tip.Sha.ShouldNotBe( initial.Tip.Sha );
            afterMerge.Tip.Tree["SomeOther.txt"].ShouldNotBeNull();
            afterMerge.Tip.Tree["Some2.txt"].ShouldNotBeNull( "Merge done: the 'Some2.txt' file is now here." );
            // This merge of branches has nothing to do with the working folder as the "test-2" is not
            // checked out.
            File.Exists( bob.WorkingFolder.AppendPart( "Some2.txt" ) ).ShouldBeFalse();
        }

        // Tim creates branch "test-3" with a commit and pushes it.
        CreateBranchAndPush( tim, 3 );

        // Fetch-merge a branch that happens to be the checked out one.
        {
            Branch initial = bob.Repository.Branches.Add( "test-3", bob.Repository.Head.Tip );
            bob.Checkout( TestHelper.Monitor, initial ).ShouldBeTrue();
            File.WriteAllText( bob.WorkingFolder.AppendPart( "SomeOther.txt" ), "Hop!" );
            bob.Commit( TestHelper.Monitor, "Bob's commit on 'test-3'." ).ShouldBe( CommitResult.Commited );
            initial = bob.Repository.Head;

            // Same as above block but we stay on the "test-3" branch.
            // And we make the working folder dirty: FetchBranch CAN work with a dirty folder.
            File.WriteAllText( bob.WorkingFolder.AppendPart( "Dirty.txt" ), "Dirty!!!" );
            bob.Repository.RetrieveStatus().IsDirty.ShouldBeTrue();

            // The remote branch is fetched: "test-2" is now tracking the 'origin' remote.
            // The "test-2" local branch has not moved, it doesn't contain the "Some2.txt" file.
            bob.FetchRemoteBranch( TestHelper.Monitor, "test-3", withTags: false, out var afterFetch ).ShouldBeTrue();
            afterFetch.ShouldNotBeNull();
            afterFetch.Tip.Sha.ShouldBe( initial.Tip.Sha );
            afterFetch.TrackedBranch.FriendlyName.ShouldBe( "origin/test-3" );
            afterFetch.Tip.Tree["Some3.txt"].ShouldBeNull();
            afterFetch.TrackedBranch.Tip.Tree["Some3.txt"].ShouldNotBeNull( "The remote branch has the 'Some3.txt' file." );

            // Now merging the remote into the local.
            // But "test-3" is currently checked out and the working folder is dirty: this fails.
            var afterMerge = afterFetch;
            bob.MergeTrackedBranch( TestHelper.Monitor, ref afterMerge ).ShouldBeFalse();
            afterMerge.ShouldBeSameAs( afterFetch );

            // Make the working folder clean.
            File.Delete( bob.WorkingFolder.AppendPart( "Dirty.txt" ) );
            bob.Repository.RetrieveStatus().IsDirty.ShouldBeFalse();

            // Now it works.
            bob.MergeTrackedBranch( TestHelper.Monitor, ref afterMerge ).ShouldBeTrue();

            // The new afterMerge has a new Tip: the merge commit.
            afterMerge.Tip.Sha.ShouldNotBe( initial.Tip.Sha );
            afterMerge.Tip.Tree["SomeOther.txt"].ShouldNotBeNull();
            afterMerge.Tip.Tree["Some3.txt"].ShouldNotBeNull( "Merge done: the 'Some3.txt' file is now here." );
            // Because the "test-3" was checked out, the Head has been updated: the working folder
            // contains the "Some3.txt" file.
            File.Exists( bob.WorkingFolder.AppendPart( "Some3.txt" ) ).ShouldBeTrue();
            bob.Repository.Head.Tip.Sha.ShouldBe( afterMerge.Tip.Sha );
        }

        static void CreateBranchAndPush( GitRepository r, int i )
        {
            var bName = $"test-{i}";
            var b = r.EnsureBranch( TestHelper.Monitor, bName ).ShouldNotBeNull();
            r.Checkout( TestHelper.Monitor, b ).ShouldBeTrue();
            r.CurrentBranchName.ShouldBe( bName );
            File.WriteAllText( r.WorkingFolder.AppendPart( $"Some{i}.txt" ), "Hello World!" );
            r.Commit( TestHelper.Monitor, $"Commit n°{i}." ).ShouldBe( CommitResult.Commited );
            r.PushBranch( TestHelper.Monitor, b, autoCreateRemoteBranch: true ).ShouldBeTrue();
        }
    }
}


