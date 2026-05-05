using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public partial class GitRepositoryTests
{
    [SetUp]
    public void Setup()
    {
        // Because we are pushing here, we need the Write PAT for the "FILESYSTEM"
        // That is useless (credentials are not used on local file system) but it's
        // good to not make an exception for this case.
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets set FILESYSTEM_GIT "don't care" --id CKli-Test""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }

    [Test]
    public void fetch_merge_push_and_pull()
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

        var bobPath = context.CurrentDirectory.AppendPart( "Bob" );
        using var bob = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, remoteUrl, true ),
                                             context.Committer,
                                             bobPath,
                                             bobPath.LastPart ).ShouldNotBeNull();

        tim.CurrentBranchName.ShouldBe( "master" );
        bob.CurrentBranchName.ShouldBe( "master" );

        // Bob fetches a branch (unknown to him) that doesn't exist: it is not an error but the resulting Branch is null.
        {
            bob.FetchRemoteBranch( TestHelper.Monitor, "kexistepas", withTags: false, out var noWay ).ShouldBeTrue();
            noWay.ShouldBeNull();
        }

        // We use use Tim to test the DeferredPushRefSpecs (for the "ckli-repo" tag) as he pushes its branches. 
        tim.DeferredPushRefSpecs.Add( "+refs/tags/ckli-repo" );
        var localRepoIdTag = tim.Repository.Tags.Add( "ckli-repo", tim.Repository.Head.Tip, tim.Committer, "I'm the repoId content.", allowOverwrite: false );
        tim.GetRemoteTags( TestHelper.Monitor, out var timTags ).ShouldBeTrue();
        timTags.IndexedTags.ContainsKey( "refs/tags/ckli-repo" ).ShouldBeFalse( "ckli-repo tag is still local only." );

        // Tim creates branch "test-1" with a commit and pushes it.
        // This pushed the "ckli-repo" tag.
        CreateBranchAndPush( tim, 1 );
        tim.DeferredPushRefSpecs.ShouldBeEmpty();
        tim.GetRemoteTags( TestHelper.Monitor, out timTags ).ShouldBeTrue();
        timTags.IndexedTags.ContainsKey( "refs/tags/ckli-repo" ).ShouldBeTrue( "The ckli-repo tag has been pushed." );

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

            // The remote branch is fetched: "test-3" is now tracking the 'origin' remote.
            // The "test-3" local branch has not moved, it doesn't contain the "Some3.txt" file.
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
    }


    // AROBAS: "Automatic Remote Origin Branch Association Strategy".
    [Test]
    public void delete_branch_and_AROBAS()
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

        var bobPath = context.CurrentDirectory.AppendPart( "Bob" );
        using var bob = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, remoteUrl, true ),
                                             context.Committer,
                                             bobPath,
                                             bobPath.LastPart ).ShouldNotBeNull();

        // Tim creates branch "test-1" with a commit and pushes it.
        CreateBranchAndPush( tim, 1 );

        // Bob hasn't fetched the remote. For him, the "test-1" branch doesn't exist.
        bob.GetBranch( TestHelper.Monitor, "test-1" ).ShouldBeNull();
        // Fetching the remotes: now bob has the branch.
        bob.FetchRemoteBranches( TestHelper.Monitor, withTags: false ).ShouldBeTrue();
        var bobBranch = bob.GetBranch( TestHelper.Monitor, "test-1" ).ShouldNotBeNull();
        // Now bob deletes it.
        bob.DeleteBranch( TestHelper.Monitor, bobBranch, DeleteGitBranchMode.WithTrackedAndRemoteBranch ).ShouldBeTrue();

        // On Tim side, the branch is here (and tracked).
        var timBranch = tim.GetBranch( TestHelper.Monitor, "test-1" ).ShouldNotBeNull();
        timBranch.IsTracking.ShouldBeTrue();
        tim.Checkout( TestHelper.Monitor, timBranch ).ShouldBeTrue();
        File.WriteAllText( tim.WorkingFolder.AppendPart( "SomeWork.txt" ), "Hop!" );
        tim.Commit( TestHelper.Monitor, "For me, test-1 exists." ).ShouldBe( CommitResult.Commited );
        // Tim fetches the remotes. Nothing can remove the defunct remote branch.
        tim.FetchRemoteBranches( TestHelper.Monitor, withTags: false ).ShouldBeTrue();
        tim.Repository.Branches["refs/remotes/origin/test-1"].ShouldNotBeNull();
        tim.MergeRemoteBranches( TestHelper.Monitor ).ShouldBeTrue();
        // Tim pushes the branch.
        tim.PushBranch( TestHelper.Monitor, timBranch, autoCreateRemoteBranch : true ).ShouldBeTrue();

        // Bob decides to resurect the "test-1" branch and work on it (this branch is not a tracking branch).
        bobBranch = bob.EnsureBranch( TestHelper.Monitor, "test-1" ).ShouldNotBeNull();
        bobBranch.IsTracking.ShouldBeFalse();
        bob.Checkout( TestHelper.Monitor, bobBranch ).ShouldBeTrue();
        File.WriteAllText( bob.WorkingFolder.AppendPart( "BobWork.txt" ), "Hop!" );
        bob.Commit( TestHelper.Monitor, "Bob's working." ).ShouldBe( CommitResult.Commited );
        bobBranch.IsTracking.ShouldBeFalse();

        // Bob "ckli pull": there's no conflict here but a merged commit is created.
        bob.FetchRemoteBranches( TestHelper.Monitor, withTags: false ).ShouldBeTrue();
        bob.MergeRemoteBranches( TestHelper.Monitor ).ShouldBeTrue();
        // ==> AROBAS here:
        bobBranch = bob.Repository.Branches["test-1"];
        bobBranch.IsTracking.ShouldBeTrue();
        bobBranch.Tip.Sha.ShouldNotBe( bob.Repository.Branches["origin/test-1"].Tip.Sha );

        // Bob pushes test-1.
        bob.PushBranch( TestHelper.Monitor, bobBranch, autoCreateRemoteBranch: true ).ShouldBeTrue();

        // Tim "ckli pull": there's no conflict and the "test-1" local branch is simply moved to the "origin/test-1".
        tim.FetchRemoteBranches( TestHelper.Monitor, withTags: false ).ShouldBeTrue();
        tim.MergeRemoteBranches( TestHelper.Monitor ).ShouldBeTrue();
        bob.Repository.Branches["test-1"].Tip.Sha.ShouldBe( bob.Repository.Branches["origin/test-1"].Tip.Sha );

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

    [Test]
    public void testing_bare_repository()
    {
        var context = TestEnv.EnsureCleanFolder();

        using var remote = CreateBareRepository( context.CurrentDirectory, "Remote", out var remoteUrl );
        using var local = CloneGitRepository( context.CurrentDirectory, "Local", remoteUrl );

        remote.RepositoryKey.IsBareRepository.ShouldBeTrue();
        local.RepositoryKey.IsBareRepository.ShouldBeFalse();

        remote.RepositoryKey.AccessKey.ReadPATKeyName.ShouldBe( GitRepositoryKey.FileSystemPrefixPAT );
        remote.RepositoryKey.AccessKey.WritePATKeyName.ShouldBe( GitRepositoryKey.FileSystemPrefixPAT );
        local.RepositoryKey.AccessKey.ReadPATKeyName.ShouldBe( GitRepositoryKey.FileSystemPrefixPAT );
        local.RepositoryKey.AccessKey.WritePATKeyName.ShouldBe( GitRepositoryKey.FileSystemPrefixPAT );

        var initialRemoteTip = remote.Repository.Head.Tip.ShouldNotBeNull();

        local.CurrentBranchName.ShouldBe( "main" );
        local.Repository.Head.Tip.Sha.ShouldBe( initialRemoteTip.Sha );
        local.Commit( TestHelper.Monitor, "Just for fun.", CommitBehavior.CreateEmptyCommit ).ShouldBe( CommitResult.Commited );
        local.PushBranch( TestHelper.Monitor, local.Repository.Head, autoCreateRemoteBranch: false );

        remote.Repository.Head.Tip.Sha.ShouldNotBe( initialRemoteTip.Sha );


        static GitRepository CreateBareRepository( NormalizedPath folder, string repositoryName, out Uri repositoryUrl )
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

        static GitRepository CloneGitRepository( NormalizedPath folder, string name, Uri remoteUrl )
        {
            var gitPath = folder.AppendPart( name );
            Directory.Exists( gitPath ).ShouldBeFalse();
            Directory.CreateDirectory( gitPath );
            var key = new GitRepositoryKey( CKliRootEnv.SecretsStore, remoteUrl, isPublic: true );
            return GitRepository.Clone( TestHelper.Monitor, key, CKliRootEnv.DefaultCKliEnv.Committer, gitPath, name ).ShouldNotBeNull();
        }


}
}


