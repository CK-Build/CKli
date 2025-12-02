using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace CKli.Core.Tests;

[TestFixture]
public class VersionTagTests
{
    [Test]
    public async Task updating_tag_content_and_push_to_remote_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "One" );
 
        // ckli clone file:///.../One-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();

        using var r = new Repository( context.CurrentDirectory.Combine( "One/OneRepo" ) );
        var origin = r.Network.Remotes["origin"];

        var t = r.Tags.Add( "test", r.Head.Tip, context.Committer, "Annotated message.", allowOverwrite: true );
        r.Network.Push( origin, t.CanonicalName );

        var t2 = r.Tags.Add( "test", r.Head.Tip, context.Committer, "Annotated message 2.", allowOverwrite: true );
        Should.Throw<LibGit2SharpException>( () => r.Network.Push( origin, t2.CanonicalName ) )
              .Message.ShouldBe( "object is no commit object" );

    }
}
