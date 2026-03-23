using NUnit.Framework;
using System.Collections.Generic;

namespace CKli.Core.Tests;

[TestFixture]
public class CommandNamespaceDescriptionTests
{
    [Test]
    public void namespace_descriptions_are_available_from_CommandNamespace()
    {
        var cmds = new Dictionary<string, Command?>();
        cmds.Add( "fix", null );
        cmds.Add( "fix start", new TestCommand( "fix start", "Start.", arguments: [], options: [], flags: [] ) );
        var descs = new Dictionary<string, string> { ["fix"] = "Fix commands." };
        var ns = CommandNamespace.UnsafeCreate( cmds, descs );

        ns.NamespaceDescriptions["fix"].ShouldBe( "Fix commands." );
        ns.NamespaceDescriptions.Count.ShouldBe( 1 );
    }

    [Test]
    public void namespace_builder_first_write_wins()
    {
        var b = new CommandNamespaceBuilder();
        b.AddNamespaceDescription( "fix", "First description." );
        b.AddNamespaceDescription( "fix", "Second description." );
        var ns = b.Build();
        ns.NamespaceDescriptions["fix"].ShouldBe( "First description." );
    }
}
