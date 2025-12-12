using CK.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public partial class TagTests
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
        // To push a tag, one can use the '+' prefix that avoids to first delete it.
        {
            var t = dev2.Tags.Add( "test", dev2.Head.Tip, context.Committer, "Annotated message 3 (from Dev2).", allowOverwrite: true );
            Should.Throw<LibGit2SharpException>( () => dev2.Network.Push( origin1, t.CanonicalName ) )
                  .Message.ShouldBe( "object is no commit object" );
            // dev2.Network.Push( origin2, ":" + t.CanonicalName );
            Should.NotThrow( () => dev2.Network.Push( origin2, "+" + t.CanonicalName ) );
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
        dev1.Tags.Add( "test", dev1.Head.Tip, context.Committer, "Annotated message (changed by Dev1 but not pushed).", allowOverwrite: true );
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
        {
            var display = DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) );
            display.ShouldBe( """
                [YELLOW]Unavailable remote tags. A 'ckli fetch' MAY enable target commits resolution for:[GRAY]⮐
                [DARKYELLOW]- refs/tags/t1-annotated, refs/tags/t1-lightweight.[GRAY]⮐
                
                """ );
        }
        // Bob sees 2 regular tags (t1) and 2 local only tags (t2).
        bob.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
        diff.Entries.Length.ShouldBe( 4 );
        {
            // One cannot rely on the sort here: the date is the same (git precision is second) and
            // commit sha is... a sha (with the commit date).
            // So map to their single local/remote tag and sort them by name.
            GitTagInfo.LocalRemoteTag[] sorted = diff.Entries.SelectMany( e => e.Tags ).OrderBy( e => e.CanonicalName ).ToArray();

            sorted[0].CanonicalName.ShouldBe( "refs/tags/t1-annotated" );
            sorted[0].Diff.ShouldBe( GitTagInfo.TagDiff.None );
            sorted[0].Commit.Message.ShouldBe( "Bob commit 1.\n" );
            sorted[0].Local?.Annotation?.Message.ShouldBe( "Bob message.\n" );
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

            var display = DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) );
            display.ShouldBe( """
                t1-annotated, t1-lightweight⮐
                [BLUE]2 local only:[GRAY]⮐
                t2-annotated, t2-lightweight⮐

                """ );
        }

        // Tim "ckli fetch". It now sees the 2 tags pushed by Bob but t1 tags are "RemoteOnly".
        tim.FetchBranches( TestHelper.Monitor, withTags: false, originOnly: true ).ShouldBeTrue();
        tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
        diff.FetchRequired.ShouldBeFalse();
        diff.UnavailableRemoteTags.ShouldBeEmpty();
        diff.Entries.Length.ShouldBe( 2 );
        {
            GitTagInfo.LocalRemoteTag[] sorted = diff.Entries.SelectMany( e => e.Tags ).OrderBy( e => e.CanonicalName ).ToArray();

            sorted[0].CanonicalName.ShouldBe( "refs/tags/t1-annotated" );
            sorted[0].Diff.ShouldBe( GitTagInfo.TagDiff.RemoteOnly );
            sorted[0].Remote?.Commit?.Sha.ShouldBe( sorted[0].Commit.Sha );
            sorted[0].Remote?.Annotation?.Message.ShouldBe( "Bob message.\n" );
            sorted[0].Commit.Message.ShouldBe( "Bob commit 1.\n" );
            sorted[0].Local.ShouldBeNull();

            sorted[1].CanonicalName.ShouldBe( "refs/tags/t1-lightweight" );
            sorted[1].Diff.ShouldBe( GitTagInfo.TagDiff.RemoteOnly );
            sorted[1].Remote!.Commit?.Sha.ShouldBe( sorted[1].Commit.Sha );
            sorted[1].Commit.Message.ShouldBe( "Bob commit 2.\n" );
            sorted[1].Local.ShouldBeNull();

            var display = DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) );
            display.ShouldBe( """
                [BLUE]2 remote only:[GRAY]⮐
                t1-annotated, t1-lightweight⮐

                """ );
        }
        // Tim now fetches the "t1-lightweight" tags.
        tim.PullTags( TestHelper.Monitor, ["t1-lightweight"] ).ShouldBeTrue();
        tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
        diff.Entries.Length.ShouldBe( 2 );
        {
            GitTagInfo.LocalRemoteTag[] sorted = diff.Entries.SelectMany( e => e.Tags ).OrderBy( e => e.CanonicalName ).ToArray();

            // No change.
            sorted[0].CanonicalName.ShouldBe( "refs/tags/t1-annotated" );
            sorted[0].Diff.ShouldBe( GitTagInfo.TagDiff.RemoteOnly );
            sorted[0].Remote?.Commit?.Sha.ShouldBe( sorted[0].Commit.Sha );
            sorted[0].Remote?.Annotation?.Message.ShouldBe( "Bob message.\n" );
            sorted[0].Commit.Message.ShouldBe( "Bob commit 1.\n" );
            sorted[0].Local.ShouldBeNull();

            // No more difference.
            sorted[1].CanonicalName.ShouldBe( "refs/tags/t1-lightweight" );
            sorted[1].Diff.ShouldBe( GitTagInfo.TagDiff.None );

            var display = DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) );
            display.ShouldBe( """
                t1-lightweight⮐
                [BLUE]1 remote only:[GRAY]⮐
                t1-annotated⮐
                
                """ );
        }
        // FetchTags is Idempotent.
        Should.NotThrow( () => tim.PullTags( TestHelper.Monitor, ["t1-lightweight"] ).ShouldBeTrue() );

        // Tim & Bob create the same "v4" tag but on 2 different commits.
        tim.SetCurrentBranch( TestHelper.Monitor, "branch1" ).ShouldBeTrue();
        bob.SetCurrentBranch( TestHelper.Monitor, "branch2" ).ShouldBeTrue();
        tim.Repository.ApplyTag( "v4" );
        bob.Repository.ApplyTag( "v4" );
        // As long as these tags remain local, everything is fine.
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight⮐
                [BLUE]1 local only:[GRAY]⮐
                v4⮐

                """ );
            bob.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight⮐
                [BLUE]3 local only:[GRAY]⮐
                t2-annotated, t2-lightweight, v4⮐

                """ );
        }
        // Tim pushes the v4 tag.
        tim.PushTags( TestHelper.Monitor, ["v4"] ).ShouldBeTrue();
        // Now Bob can see a conflict.
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight, v4⮐

                """ );
            bob.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            RedactCommitId( DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ) ).ShouldBe( """
                [RED]⚠ 1 conflicts:[GRAY]⮐
                [DARKRED]- Tag 'v4' is locally on '[Redacted]' but targets '[Redacted]' on the remote.[GRAY]⮐
                t1-annotated, t1-lightweight⮐
                [BLUE]2 local only:[GRAY]⮐
                t2-annotated, t2-lightweight⮐

                """ );
        }
        // And decides that he is right: he pushes it.
        bob.PushTags( TestHelper.Monitor, ["v4"] ).ShouldBeTrue();
        // Now it is Tim that sees a conflict.
        // And this is an interesting case: Tim has not locally tracked the Bob's branch2, the
        // branch2's Tip is not in Tim's local repository and this is a conflict... but with an
        // unavailable commit.
        {
            bob.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight, v4⮐
                [BLUE]2 local only:[GRAY]⮐
                t2-annotated, t2-lightweight⮐

                """ );
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            RedactCommitId( DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ) ).ShouldBe( """
                [YELLOW]Unavailable remote tags. A 'ckli fetch' MAY enable target commits resolution for:[GRAY]⮐
                [DARKYELLOW]- refs/tags/v4.[GRAY]⮐
                [RED]⚠ 1 conflicts:[GRAY]⮐
                [DARKRED]- Tag 'v4' is locally on '[Redacted]' but targets 'unavailable' on the remote.[GRAY]⮐
                t1-annotated, t1-lightweight⮐

                """ );
        }
        //
        // Tim tries to resolve this unavailable commit with a 'ckli fetch'.
        // No luck: the remote branch2 is considered on-par with Tim's "origin/branch2', git doesn't
        // follow the "v4" tag that is a "new" one.
        // ... No change. But we know that is v4 tag is problematic.
        //
        tim.FetchBranches( TestHelper.Monitor, withTags: false, originOnly: true ).ShouldBeTrue();
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            RedactCommitId( DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ) ).ShouldBe( """
                [YELLOW]Unavailable remote tags. A 'ckli fetch' MAY enable target commits resolution for:[GRAY]⮐
                [DARKYELLOW]- refs/tags/v4.[GRAY]⮐
                [RED]⚠ 1 conflicts:[GRAY]⮐
                [DARKRED]- Tag 'v4' is locally on '[Redacted]' but targets 'unavailable' on the remote.[GRAY]⮐
                t1-annotated, t1-lightweight⮐

                """ );
        }
        // Tim 'ckli tag pull v4'
        // => Its "v4" is lost, Bob's V4 on branch1 is the winner.
        tim.PullTags( TestHelper.Monitor, ["v4"] ).ShouldBeTrue();
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            RedactCommitId( DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ) ).ShouldBe( """
                t1-annotated, t1-lightweight, v4⮐

                """ );
        }
        // And rewrite it to be an annotated tag (v4 is on Bob's still local branch2's Tip).
        {
            var t4 = tim.Repository.Tags["v4"];
            tim.Repository.Tags.Add( "v4",
                                     t4.Target,
                                     new Signature( "Tim", "tim@mail.com", new DateTimeOffset( 2000, 1, 1, 0, 0, 0, TimeSpan.Zero ) ),
                                     "I'm annotated.",
                                     allowOverwrite: true );
        }
        // There's no "conflict", only a difference with Bob's lightweight tag:
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight⮐
                [MAGENTA]1 differences:[GRAY]⮐
                - 'v4' is locally an annotated tag but remotely a lightweight one.⮐

                """ );
        }
        // Bob rewrite its v4 to also be an annotated tag and pushes it.
        {
            var t4 = bob.Repository.Tags["v4"];
            bob.Repository.Tags.Add( "v4",
                                     t4.Target,
                                     new Signature( "Bob", "bob@mail.com", new DateTimeOffset( 2025, 12, 11, 0, 0, 0, TimeSpan.Zero ) ),
                                     "I'm annotated too.",
                                     allowOverwrite: true );
            bob.PushTags( TestHelper.Monitor, ["v4"] ).ShouldBeTrue();
        }
        // I wish Tim could see a different difference... But as this is an annotated object that is not locally known,
        // we are back to the "Unavailable" tag case :-(.
        // It seems that following only the refs (without actually replacing local object) is not possible.
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            RedactCommitId( DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ) ).ShouldBe( """
                [YELLOW]Unavailable remote tags. A 'ckli fetch' MAY enable target commits resolution for:[GRAY]⮐
                [DARKYELLOW]- refs/tags/v4.[GRAY]⮐
                [RED]⚠ 1 conflicts:[GRAY]⮐
                [DARKRED]- Tag 'v4' is locally on '[Redacted]' but targets 'unavailable' on the remote.[GRAY]⮐
                t1-annotated, t1-lightweight⮐

                """ );
        }
        // Tim has no other choice to pull the v4 tag...
        tim.PullTags( TestHelper.Monitor, ["v4"] ).ShouldBeTrue();
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight, v4⮐

                """ );
        }
        // So let's check the modification display from Tim's side only...
        {
            var t4 = tim.Repository.Tags["v4"];
            tim.Repository.Tags.Add( "v4",
                                     t4.Target,
                                     new Signature( "Tim", "tim@mail.com", new DateTimeOffset( 2000, 1, 1, 0, 0, 0, TimeSpan.Zero ) ),
                                     "I'm annotated.",
                                     allowOverwrite: true );
        }
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight⮐
                [MAGENTA]1 differences:[GRAY]⮐
                - 'v4' has:⮐
                │ <local message>⮐
                │  I'm annotated.⮐
                │ <remote message>⮐
                │  I'm annotated too.⮐
                │ <Local tagger>⮐
                │  Tim (tim@mail.com)⮐
                │  on 2000-01-01 00:00:00Z⮐
                │ <remote signature>⮐
                │  Bob (bob@mail.com)⮐
                │  on 2025-12-11 00:00:00Z⮐

                """ );
        }
        {
            var t4 = tim.Repository.Tags["v4"];
            tim.Repository.Tags.Add( "v4",
                                     t4.Target,
                                     new Signature( "Bob", "bob@mail.com", new DateTimeOffset( 2025, 12, 11, 0, 0, 0, TimeSpan.Zero ) ),
                                     "I'm annotated.",
                                     allowOverwrite: true );
        }
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight⮐
                [MAGENTA]1 differences:[GRAY]⮐
                - 'v4' has:⮐
                │ <local message>⮐
                │  I'm annotated.⮐
                │ <remote message>⮐
                │  I'm annotated too.⮐

                """ );
        }
        {
            var t4 = tim.Repository.Tags["v4"];
            tim.Repository.Tags.Add( "v4",
                                     t4.Target,
                                     t4.Annotation!.Tagger,
                                     "I'm annotated too.",
                                     allowOverwrite: true );
        }
        {
            tim.GetDiffTags( TestHelper.Monitor, out diff ).ShouldBeTrue();
            DebugRenderer.Render( diff.ToRenderable( ScreenType.Default, orderByTagName: true ) ).ShouldBe( """
                t1-annotated, t1-lightweight, v4⮐

                """ );
        }

    }

    static string RedactCommitId( string text )
    {
        return _rCommitId().Replace( text, "'[Redacted]'" ); 
    }

    [GeneratedRegex( "'[0-9a-f]{5,}'" )]
    private static partial Regex _rCommitId();
}


