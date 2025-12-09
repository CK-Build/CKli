using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class TagTests
{
    [Test]
    public async Task TagFetchMode_All_must_not_be_the_default_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "One" );
        var remoteUrl = remotes.GetUriFor( "OneRepo" ).ToString();

        var repo1 = context.CurrentDirectory.AppendPart( "Dev1" );
        using var dev1 = new Repository( Repository.Clone( remoteUrl, repo1 ) );
        var origin1 = dev1.Network.Remotes["origin"];

        // Dev1 creates a tag and pushes it.
        // She can modify it locally but pushing it fails: the remote tag must first be deleted.
        {
            var t = dev1.Tags.Add( "test", dev1.Head.Tip, context.Committer, "Annotated message.", allowOverwrite: true );
            dev1.Network.Push( origin1, t.CanonicalName );

            var t2 = dev1.Tags.Add( "test", dev1.Head.Tip, context.Committer, "Annotated message 2.", allowOverwrite: true );
            t2.CanonicalName.ShouldBe( t.CanonicalName );
            Should.Throw<LibGit2SharpException>( () => dev1.Network.Push( origin1, t2.CanonicalName ) )
                  .Message.ShouldBe( "object is no commit object" );

            dev1.Network.Push( origin1, ":" + t.CanonicalName );
            // A deletion is fortunately idempotent.
            Should.NotThrow( () => dev1.Network.Push( origin1, ":" + t.CanonicalName ) );

            // This updates the remote tag.
            Should.NotThrow( () => dev1.Network.Push( origin1, t2.CanonicalName ) );
        }
        // Dev2 clones the repository.
        // He has the updated message and decides to update it.
        var repo2 = context.CurrentDirectory.AppendPart( "Dev2" );
        using var dev2 = new Repository( Repository.Clone( remoteUrl, repo2 ) );
        var origin2 = dev2.Network.Remotes["origin"];
        // Messages are normalized to end with a newline.
        dev2.Tags["test"].Annotation.Message.ReplaceLineEndings().ShouldBe( """
            Annotated message 2.

            """ );
        {
            var t = dev2.Tags.Add( "test", dev2.Head.Tip, context.Committer, "Annotated message 3 (from Dev2).", allowOverwrite: true );
            Should.Throw<LibGit2SharpException>( () => dev2.Network.Push( origin1, t.CanonicalName ) )
                  .Message.ShouldBe( "object is no commit object" );
            dev2.Network.Push( origin2, ":" + t.CanonicalName );
            Should.NotThrow( () => dev2.Network.Push( origin2, t.CanonicalName ) );
        }

        // Dev1 now fetches all the branches...
        // With all the tags?... Not that easy :-( but this seems to work.
        // https://stackoverflow.com/questions/1204190/does-git-fetch-tags-include-git-fetch
        {
            IEnumerable<string> refSpecs = origin1.FetchRefSpecs.Select( x => x.Specification );
            Commands.Fetch( dev1, origin1.Name, refSpecs, new FetchOptions()
            {
                TagFetchMode = TagFetchMode.All
            }, "Dev1 fetch." );
        }
        // The dev1 tag now exists!
        dev1.Tags["test"].Annotation.Message.ReplaceLineEndings().ShouldBe( """
            Annotated message 3 (from Dev2).

            """ );

        // Dev1 changes the tag locally.
        dev1.Tags.Add( "test", dev1.Head.Tip, context.Committer, "Annotated message (changed by De1 but not pushed).", allowOverwrite: true );
        // And fetches again.
        {
            IEnumerable<string> refSpecs = origin1.FetchRefSpecs.Select( x => x.Specification );
            Commands.Fetch( dev1, origin1.Name, refSpecs, new FetchOptions()
            {
                TagFetchMode = TagFetchMode.All
            }, "Dev1 fetch." );
        }
        // The dev1 tag is updated: the local modifications are lost.
        //
        // => TagFetchMode.All MUST NOT be the default!
        //
        dev1.Tags["test"].Annotation.Message.ReplaceLineEndings().ShouldBe( """
            Annotated message 3 (from Dev2).

            """ );
    }

    [Test]
    public async Task with_TagFetchMode_None_tags_can_managed_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "One" );
        var remoteUrl = remotes.GetUriFor( "OneRepo" ).ToString();

        var timPath = context.CurrentDirectory.AppendPart( "Tim" );
        using var tim = new Repository( Repository.Clone( remoteUrl, timPath ) );
        var timOrigin = tim.Network.Remotes["origin"];

        var bobPath = context.CurrentDirectory.AppendPart( "Bob" );
        using var bob = new Repository( Repository.Clone( remoteUrl, bobPath ) );
        var bobOrigin = bob.Network.Remotes["origin"];
        // Bob creates "branch1" and "branch2" with 3 commits (first and second tagged) and pushes the branches.
        {
            var (branch1, t11, t12) = CreateBranch( context, bobPath, bob, "Bob", "branch1", "t1", true );
            var (branch2, t21, t22) = CreateBranch( context, bobPath, bob, "Bob", "branch2", "t2", true );
        }
        // One can list the tags (name) but not the content of an Annotated tag
        // when the commits have not been fetched.
        var refBefore = tim.Network.ListReferences( timOrigin );
        foreach( var r in refBefore )
        {
            if( r.IsTag )
            {
                var dr = r.ResolveToDirectReference();
                if( dr.Target == null )
                {
                    dr.TargetIdentifier.ShouldNotBeNull( "But we have the sha of the annotated tag or the commit." );
                }
            }
        }
        // Tim fetch branches with TagFetchMode.None.
        {
            IEnumerable<string> refSpecs = timOrigin.FetchRefSpecs.Select( x => x.Specification );
            Commands.Fetch( tim, "origin", refSpecs, new FetchOptions()
            {
                TagFetchMode = TagFetchMode.None
            }, logMessage: "Tim" );
            // No tags.
            tim.Tags.ShouldBeEmpty();
        }
        var references = tim.Network.ListReferences( timOrigin );
        foreach( var r in references )
        {
            if( r.IsTag )
            {
                var dr = r.ResolveToDirectReference();
                var target = dr.Target.ShouldNotBeNull();
                if( target is TagAnnotation a )
                {
                    a.Message.ReplaceLineEndings().ShouldBe( """
                        Bob message.

                        """ );
                    a.Target.ShouldBeAssignableTo<Commit>();
                }
                else
                {
                    dr.Target.ShouldBeAssignableTo<Commit>();
                }
            }
        }
    }

    // Creates a branch with 3 commits (first annotated tag and second with a lightweight tag).
    static (Branch B, Tag T1, Tag T2) CreateBranch( CKliEnv context,
                                                    NormalizedPath path,
                                                    Repository r,
                                                    string devName,
                                                    string branchName,
                                                    string tagPrefix,
                                                    bool push )
    {
        GitRepository.IsCKliValidTagName( tagPrefix ).ShouldBeTrue();

        var branch = r.CreateBranch( branchName );
        Commands.Checkout( r, branch );

        var someFilePath = path.AppendPart( $"Created-By-{devName}.txt" );

        File.WriteAllText( someFilePath, $"{devName} 1" );
        Commands.Stage( r, someFilePath.LastPart );
        r.Commit( $"{devName} commit 1.", context.Committer, context.Committer );
        var t1 = r.Tags.Add( $"{tagPrefix}-annotated", r.Head.Tip, context.Committer, $"{devName} message.", allowOverwrite: false );

        File.WriteAllText( someFilePath, $"{devName} 2" );
        Commands.Stage( r, someFilePath.LastPart );
        r.Commit( $"{devName} commit 2.", context.Committer, context.Committer );
        var t2 = r.Tags.Add( $"{tagPrefix}-lightweight", r.Head.Tip );

        File.WriteAllText( someFilePath, $"{devName} 3" );
        Commands.Stage( r, someFilePath.LastPart );
        r.Commit( $"{devName} commit 3.", context.Committer, context.Committer );
        if( push )
        {
            // Associates the remote branch and pushes it.
            r.Branches.Update( branch, u => { u.Remote = "origin"; u.UpstreamBranch = branch.CanonicalName; } );
            r.Network.Push( branch );
            // Pushes the tags.
            r.Network.Push( r.Network.Remotes["origin"], [t1.CanonicalName, t2.CanonicalName] );
        }
        return (branch, t1, t2);
    }

    [Test]
    public async Task GitTagInfo_Diff_test_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "One" );
        var remoteUrl = remotes.GetUriFor( "OneRepo" );

        var timPath = context.CurrentDirectory.AppendPart( "Tim" );
        using var tim = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, remoteUrl, true ),
                                             timPath,
                                             timPath.LastPart ).ShouldNotBeNull();

        var bobPath = context.CurrentDirectory.AppendPart( "Bob" );
        using var bob = GitRepository.Clone( TestHelper.Monitor,
                                             new GitRepositoryKey( context.SecretsStore, remoteUrl, true ),
                                             bobPath,
                                             bobPath.LastPart ).ShouldNotBeNull();

        // Bob creates "branch1" and "branch2" with 3 commits (first and second tagged).
        // "branch1" is pushed, "branch2" remains local.
        {
            var (branch1, t11, t12) = CreateBranch( context, bobPath, bob.Repository, "Bob", "branch1", "t1", true );
            var (branch2, t21, t22) = CreateBranch( context, bobPath, bob.Repository, "Bob", "branch2", "t2", false );
        }

        // Tim sees 2 "fetch required" tags.
        tim.GetDiffTags( TestHelper.Monitor, out var diff ).ShouldBeTrue();
        diff.Entries.ShouldBeEmpty();
        diff.FetchRequired.ShouldBeTrue();
        diff.UnavailableRemoteTags.ShouldBe( ["refs/tags/t1-annotated", "refs/tags/t1-lightweight"] );

        // Bob sees 2 regular tags (t1) and 2 local only tags (t2).
        bob.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
        diff.Entries.Length.ShouldBe( 4 );
        // One cannot rely on the sort here: the date is the same (git precision is second) and
        // commit sha is... a sha (with the commit date).
        // So map to their single local/remote tag and sort them by name.
        GitTagInfo.LocalRemoteTag[] sorted = diff.Entries.SelectMany( e => e.Tags ).OrderBy( e => e.CanonicalName ).ToArray();

        sorted[0].CanonicalName.ShouldBe( "refs/tags/t1-annotated" );
        sorted[0].Diff.ShouldBe( GitTagInfo.TagDiff.None );
        sorted[0].Commit.Message.ShouldBe( "Bob commit 1.\n" );
        sorted[0].Local?.Commit?.Sha.ShouldBe( sorted[0].Commit.Sha );
        sorted[0].Remote?.Commit?.Sha.ShouldBe( sorted[0].Commit.Sha );

        sorted[1].CanonicalName.ShouldBe( "refs/tags/t1-lightweight" );
        sorted[1].Diff.ShouldBe( GitTagInfo.TagDiff.None );
        sorted[1].Commit.Message.ShouldBe( "Bob commit 2.\n" );
        sorted[1].Local?.Commit?.Sha.ShouldBe( sorted[1].Commit.Sha );
        sorted[1].Remote?.Commit?.Sha.ShouldBe( sorted[1].Commit.Sha );

        sorted[2].CanonicalName.ShouldBe( "refs/tags/t2-annotated" );
        sorted[2].Diff.ShouldBe( GitTagInfo.TagDiff.LocalOnly );
        sorted[2].Commit.Message.ShouldBe( "Bob commit 1.\n" );
        sorted[2].Local?.Commit?.Sha.ShouldBe( sorted[2].Commit.Sha );
        sorted[2].Remote.ShouldBeNull();

        sorted[3].CanonicalName.ShouldBe( "refs/tags/t2-lightweight" );
        sorted[3].Diff.ShouldBe( GitTagInfo.TagDiff.LocalOnly );
        sorted[3].Commit.Message.ShouldBe( "Bob commit 2.\n" );
        sorted[3].Local?.Commit?.Sha.ShouldBe( sorted[3].Commit.Sha );
        sorted[3].Remote.ShouldBeNull();

    }
}


