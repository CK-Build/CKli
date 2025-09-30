using CK.Core;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class PluginTests
{
    [Test]
    public void create_plugin_request_Info_and_remove_it()
    {
        var localPath = ClonedPaths.EnsureCleanFolder();
        var secretsStore = new DotNetUserSecretsStore();
        var remotes = TestEnv.UseReadOnly( "One" );

        // ckli clone file:///.../One-Stack
        CKliCommands.Clone( TestHelper.Monitor, secretsStore, localPath, remotes.StackUri ).ShouldBe( 0 );
        // cd One
        localPath = localPath.AppendPart( "One" );

        CKliCommands.PluginCreate( TestHelper.Monitor, secretsStore, localPath, "MyFirstOne" ).ShouldBe( 0 );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            StackRepository.OpenWorldFromPath( TestHelper.Monitor, secretsStore, localPath, out var stack, out var world, skipPullStack: true )
                           .ShouldBeTrue();
            try
            {
                world.RaisePluginInfo( TestHelper.Monitor, out var text ).ShouldBeTrue();
                text.ShouldContain( "1 loaded plugins, 1 configured plugins." );
                text.ShouldContain( "Message from 'MyFirstOne' plugin." );
                logs.ShouldContain( "New 'MyFirstOne' in world 'One' plugin certainly requires some development." );
            }
            finally
            {
                stack.Dispose();
            }
        }

        CKliCommands.PluginRemove( TestHelper.Monitor, secretsStore, localPath, "MyFirstOne" ).ShouldBe( 0 );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            CKliCommands.PluginInfo( TestHelper.Monitor, secretsStore, localPath ).ShouldBe( 0 );
            logs.ShouldNotContain( "New 'MyFirstOne' in world 'One' plugin certainly requires some development." );
        }


    }
}
