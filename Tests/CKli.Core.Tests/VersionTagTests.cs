using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core.Tests;

[TestFixture]
public class VersionTagTests
{
    [Test]
    public async Task updating_tag_content_and_push_to_remote_Async()
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
        // The dev1 tag is updated!
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
        dev1.Tags["test"].Annotation.Message.ReplaceLineEndings().ShouldBe( """
            Annotated message 3 (from Dev2).

            """ );

        // When a tag is updated (not created), we should delete it from the remote and pushes
        // it back... But only IF it the tag exists on the remote...
        // How can we know if a tag is "from" the remote or a previously locally created tag?
        // it seems that we can't. But if we track the newly created tags and updated tags,
        // we can maintain the information... But if a fetch is done, we can have a remote tag
        // that replaces our locally created (or updated) tag...
        // This is true in the general case but a version tag targets a commit. And the content of
        // the tag is always the same (it depends on the commit). Either 2 systems
        // concurrently build:
        // - the same commit with the same version. The tag's message will be the same.
        // - 2 different commits with the same version: this kind of conflict is far above
        //   the "update a tag" discussion.
        // The other use case is when versioned tags are reprocessed (migrations). This occurs
        // once in a while and when this happens, we should ensure that this fix is done on the "original"
        // repository, before any local work is done in it. If this is the case, the "Versioned Tag fix"
        // can delete the remote tags and push the updated ones to the remote.
        // Actually it SHOULD update the remote this way: only the tags are updated, no branches, no new
        // commits appear on the remote.
    }
}
