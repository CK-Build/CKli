using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// The "CKliVersion" attribute of a World definition file pins the CKli version that World is frozen on.
/// <para>
/// It exists because the plugin solution no longer records it: "CKli.Plugins.Core" and the Standard Plugins are
/// referenced at $(CKliVersion) and that property comes from a generated, git ignored "CKli.Version.props". A
/// World that must impose a version therefore has to say so here - this is what "ckli world lts create" writes.
/// </para>
/// <para>
/// <b>What this fixture cannot cover:</b> the refusal of a mismatched pin. <see cref="World.CKliVersion"/> is
/// read from the assembly's InformationalVersion and a locally compiled CKli - which is what every test run
/// uses - is <see cref="SVersion.ZeroVersion"/>, for which the check is deliberately skipped (a developer
/// building CKli must be able to open any World). Only the syntax check and that escape hatch are reachable
/// from here; the mismatch refusal is verified by hand.
/// </para>
/// </summary>
[TestFixture]
public class WorldCKliVersionPinTests
{
    [Test]
    public void a_test_run_always_uses_a_locally_compiled_CKli()
    {
        // This is the precondition of the 2 tests below: if it ever stops holding, they stop testing
        // what they claim to and the mismatch refusal becomes reachable.
        World.CKliVersion.Version.ShouldBe( SVersion.ZeroVersion );
    }

    /// <summary>
    /// The syntax is checked before the escape hatch below, so this one is reachable: a World that states an
    /// unparsable version must be fixed rather than silently opened.
    /// </summary>
    [Test]
    public async Task an_invalid_CKliVersion_prevents_the_world_to_be_loaded_Async()
    {
        var context = await CloneOneAsync();
        WriteLTSWorld( context, "@net8", ckliVersion: "not-a-version" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            using var stack = OpenStack( context );
            stack.WorldNames.Single( w => !w.IsDefaultWorld )
                 .LoadDefinitionFile( TestHelper.Monitor )
                 .ShouldBeNull();
            logs.ShouldContain( l => l.Contains( """CKliVersion="not-a-version" """.TrimEnd() ) );
        }
    }

    /// <summary>
    /// A locally compiled CKli ignores the pin: without this, a developer building CKli could not open any
    /// pinned World at all. The World loads and its <see cref="WorldDefinitionFile.PinnedCKliVersion"/> is
    /// the stated one (it is what the plugin solution would then be built against).
    /// </summary>
    [Test]
    public async Task a_CKliVersion_pin_is_ignored_by_a_locally_compiled_CKli_Async()
    {
        var context = await CloneOneAsync();
        WriteLTSWorld( context, "@net8", ckliVersion: "99.99.99" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            using var stack = OpenStack( context );
            var definitionFile = stack.WorldNames.Single( w => !w.IsDefaultWorld )
                                      .LoadDefinitionFile( TestHelper.Monitor )
                                      .ShouldNotBeNull();
            definitionFile.PinnedCKliVersion.ShouldBe( SVersion.Parse( "99.99.99" ) );
            logs.ShouldContain( l => l.Contains( "Using locally compiled CKli (version 0.0.0-0)" ) );
        }
    }

    /// <summary>
    /// A World without the attribute is not pinned: this is the default World's normal state, where each
    /// developer keeps its own CKli version.
    /// </summary>
    [Test]
    public async Task no_CKliVersion_attribute_means_no_pin_Async()
    {
        var context = await CloneOneAsync();

        using var stack = OpenStack( context );
        stack.WorldNames.Single( w => w.IsDefaultWorld )
             .LoadDefinitionFile( TestHelper.Monitor )
             .ShouldNotBeNull()
             .PinnedCKliVersion.ShouldBeNull();
    }

    /// <summary>
    /// "ckli world lts create" pins the World it creates - except when CKli is locally compiled: writing
    /// "0.0.0-0" would produce a World that nobody can open (no one can install that version) and that only a
    /// manual edit could repair. A test run is always in that case, hence the warning rather than the pin.
    /// </summary>
    [Test]
    public async Task world_lts_create_does_not_write_a_0_0_0_0_pin_Async()
    {
        var context = await CloneOneAsync();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "world", "lts", "create", "@net8" )).ShouldBeTrue();
            logs.ShouldContain( l => l.Contains( "is created" ) && l.Contains( "without its CKliVersion pin" ) );
        }
        var ltsRoot = XDocument.Load( StackFolder( context ).AppendPart( "One@net8.xml" ) ).Root.ShouldNotBeNull();
        ltsRoot.Attribute( XNames.LTSName ).ShouldNotBeNull().Value.ShouldBe( "@net8" );
        ltsRoot.Attribute( XNames.CKliVersion ).ShouldBeNull();
    }

    // The CallerMemberName is the calling test's name: without it every test here would share this
    // helper's name as its "Cloned/<test-name>" folder.
    static async Task<CKliEnv> CloneOneAsync( [CallerMemberName] string? name = null )
    {
        var context = TestEnv.EnsureCleanFolder( name );
        var one = TestEnv.OpenRemotes( "One" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", one.StackUri )).ShouldBeTrue();
        return context.ChangeDirectory( "One" );
    }

    static StackRepository OpenStack( CKliEnv context )
    {
        return StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                              .ShouldNotBeNull();
    }

    static NormalizedPath StackFolder( CKliEnv context ) => context.CurrentDirectory.AppendPart( ".PublicStack" );

    static void WriteLTSWorld( CKliEnv context, string ltsName, string? ckliVersion )
    {
        var fileName = $"One@{ltsName[1..]}.xml";
        var attributes = ckliVersion != null ? $""" CKliVersion="{ckliVersion}" """.TrimEnd() : "";
        var path = StackFolder( context ).AppendPart( fileName );
        File.WriteAllText( path,
                           $"""
                            <One LTSName="{ltsName}"{attributes}>
                              <Repository Url="OneRepo" />
                            </One>
                            """ );
        using var stack = OpenStack( context );
        stack.Commit( TestHelper.Monitor, $"Added '{ltsName}' world." ).ShouldBeTrue();
    }
}
