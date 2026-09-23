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
/// <b>The two modes.</b> <see cref="World.CKliVersion"/> is read from this assembly's InformationalVersion and
/// both of its branches are live here. A plain "dotnet test" stamps no version, so it is
/// <see cref="SVersion.ZeroVersion"/> and the pin is deliberately ignored (a developer building CKli must be
/// able to open any World). But when CKli builds itself - "ckli publish" on this very Stack - the solution is
/// built with the released version, the escape hatch closes and the mismatch refusal becomes reachable. No test
/// here may assume one mode: each covers the branch that applies, through <see cref="LocallyCompiled"/>.
/// </para>
/// </summary>
[TestFixture]
public class WorldCKliVersionPinTests
{
    // Which of the 2 modes above this run is in. This used to be an assumption of this fixture ("a test run
    // always uses a locally compiled CKli") until CKli built itself and the 3 tests that relied on it failed.
    static bool LocallyCompiled => World.CKliVersion.Version == SVersion.ZeroVersion;

    // "ckli world lts create" takes the "publish" and "lts" locks: it pushes lock references to the "file://" remote.
    [OneTimeSetUp]
    public void OneTimeSetup() => TestEnv.SetFileSystemWritePAT();

    [OneTimeTearDown]
    public void OneTimeTearDown() => TestEnv.RemoveFileSystemWritePAT();

    /// <summary>
    /// The syntax is checked before the escape hatch below, so this one is reachable in both modes: a World that
    /// states an unparsable version must be fixed rather than silently opened.
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
    /// A pin that states the running version always opens. This is the nominal case of a LTS world and the only
    /// assertion here that is the same in both modes: a locally compiled CKli takes the escape hatch below, a
    /// released one finds the versions equal.
    /// </summary>
    [Test]
    public async Task a_matching_CKliVersion_pin_opens_the_world_Async()
    {
        var running = World.CKliVersion.Version.ShouldNotBeNull();
        var context = await CloneOneAsync();
        WriteLTSWorld( context, "@net8", ckliVersion: running.ToString() );

        using var stack = OpenStack( context );
        stack.WorldNames.Single( w => !w.IsDefaultWorld )
             .LoadDefinitionFile( TestHelper.Monitor )
             .ShouldNotBeNull()
             .PinnedCKliVersion.ShouldBe( running );
    }

    /// <summary>
    /// The LTS guard itself. A released CKli refuses a World pinned to another version. A locally compiled one
    /// ignores the pin instead: without this escape hatch a developer building CKli could not open any pinned
    /// World at all - the World then loads and its <see cref="WorldDefinitionFile.PinnedCKliVersion"/> is the
    /// stated one (it is what the plugin solution would then be built against).
    /// </summary>
    [Test]
    public async Task a_mismatched_CKliVersion_pin_is_refused_unless_CKli_is_locally_compiled_Async()
    {
        var context = await CloneOneAsync();
        WriteLTSWorld( context, "@net8", ckliVersion: "99.99.99" );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            using var stack = OpenStack( context );
            var definitionFile = stack.WorldNames.Single( w => !w.IsDefaultWorld )
                                      .LoadDefinitionFile( TestHelper.Monitor );
            if( LocallyCompiled )
            {
                definitionFile.ShouldNotBeNull().PinnedCKliVersion.ShouldBe( SVersion.Parse( "99.99.99" ) );
                logs.ShouldContain( l => l.Contains( "Using locally compiled CKli (version 0.0.0-0)" ) );
            }
            else
            {
                definitionFile.ShouldBeNull();
                logs.ShouldContain( l => l.Contains( "This world is pinned to CKli version" )
                                         && l.Contains( "99.99.99" )
                                         && l.Contains( $"This CKli version is '{World.CKliVersion.Version}'" ) );
            }
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
    /// manual edit could repair. Hence the warning rather than the pin in that mode.
    /// </summary>
    [Test]
    public async Task world_lts_create_pins_the_new_world_unless_CKli_is_locally_compiled_Async()
    {
        var context = await CloneOneAsync();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "world", "lts", "create", "@net8" )).ShouldBeTrue();
            if( LocallyCompiled )
            {
                logs.ShouldContain( l => l.Contains( "is created" ) && l.Contains( "without its CKliVersion pin" ) );
            }
        }
        var ltsRoot = XDocument.Load( StackFolder( context ).Combine( "@net8/One@net8.xml" ) ).Root.ShouldNotBeNull();
        ltsRoot.Attribute( XNames.LTSName ).ShouldNotBeNull().Value.ShouldBe( "@net8" );
        var pin = ltsRoot.Attribute( XNames.CKliVersion );
        if( LocallyCompiled )
        {
            pin.ShouldBeNull();
        }
        else
        {
            SVersion.Parse( pin.ShouldNotBeNull().Value ).ShouldBe( World.CKliVersion.Version );
        }
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
        var attributes = ckliVersion != null ? $""" CKliVersion="{ckliVersion}" """.TrimEnd() : "";
        // A LTS world definition file is in the world's own "@ltsName/" folder of the Stack repository.
        var path = StackFolder( context ).AppendPart( ltsName ).AppendPart( $"One{ltsName}.xml" );
        Directory.CreateDirectory( path.RemoveLastPart() );
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
