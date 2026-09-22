using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// A World's plugin solution references "CKli.Plugins.Core" and the Standard Plugins at $(CKliVersion), a
/// property carried by a generated, git ignored "CKli.Version.props" beside its "Directory.Build.props".
/// <para>
/// The point is that the CKli version each developer runs is no longer written into a tracked file: it used to
/// be a literal version rewritten on every World open, which left the Stack repository dirty and made the next
/// "ckli pull" fail for a colleague on another CKli version.
/// </para>
/// <para>
/// A Stack created before this carries literal versions and a "Directory.Build.props" with no import at all:
/// the migration below has to do <b>both</b>, since rewriting the versions alone would leave $(CKliVersion)
/// undefined and nothing would restore.
/// </para>
/// </summary>
[TestFixture]
public class PluginSolutionCKliVersionTests
{
    // The "Directory.Build.props" and "Directory.Packages.props" exactly as CKli wrote them before
    // $(CKliVersion) existed. This is what a Stack created by an older CKli holds.
    const string PreMigrationDirectoryBuildProps = """
        <Project>
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ArtifactsPath>$(MSBuildThisFileDirectory)../$Local/One-Plugins</ArtifactsPath>
            <ArtifactsPivots>run</ArtifactsPivots>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>

        """;

    const string PreMigrationDirectoryPackageProps = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="CKli.Plugins.Core" Version="0.0.0-0" />
          </ItemGroup>
        </Project>

        """;

    [Test]
    public async Task a_new_plugin_solution_uses_the_CKliVersion_property_Async()
    {
        var (context, plugins) = await CloneAndOpenAsync();

        File.ReadAllText( plugins.AppendPart( "Directory.Packages.props" ) )
            .ShouldContain( """<PackageVersion Include="CKli.Plugins.Core" Version="$(CKliVersion)" />""" );

        File.ReadAllText( plugins.AppendPart( "Directory.Build.props" ) )
            .ShouldContain( "CKli.Version.props", Case.Sensitive,
                            "The generated file must be imported, otherwise $(CKliVersion) is empty." );

        File.ReadAllText( plugins.AppendPart( PluginMachinery.CKliVersionPropsFileName ) )
            .ShouldContain( $"<CKliVersion>{World.CKliVersion.Version}</CKliVersion>" );
    }

    /// <summary>
    /// The generated file is not tracked: this is the whole point, since it is the CKli version of whoever
    /// happens to run the command.
    /// </summary>
    [Test]
    public async Task the_generated_CKli_Version_props_is_git_ignored_Async()
    {
        var (context, plugins) = await CloneAndOpenAsync();

        File.ReadAllText( context.CurrentDirectory.Combine( ".PublicStack/.gitignore" ) )
            .ShouldContain( PluginMachinery.CKliVersionPropsFileName );

        // The file exists but git must not see it: it holds the CKli version of whoever ran the command, and
        // a tracked file that every developer rewrites is exactly what used to break the next "ckli pull".
        File.Exists( plugins.AppendPart( PluginMachinery.CKliVersionPropsFileName ) ).ShouldBeTrue();

        using var stack = StackRepository.TryOpenFromPath( TestHelper.Monitor, context, out _, skipPullStack: true )
                                         .ShouldNotBeNull();
        // The pattern is unanchored on purpose (a LTS World's solution is in a sub folder of the Stack), so
        // this checks the path that is actually used rather than the pattern alone.
        stack.GitRepository.Repository.Ignore
             .IsPathIgnored( $"One-Plugins/{PluginMachinery.CKliVersionPropsFileName}" )
             .ShouldBeTrue();
        // The rest of the plugin solution is tracked: only the version file is not.
        stack.GitRepository.Repository.Ignore.IsPathIgnored( "One-Plugins/Directory.Build.props" ).ShouldBeFalse();
        stack.GitRepository.Repository.Ignore.IsPathIgnored( "One-Plugins/Directory.Packages.props" ).ShouldBeFalse();
    }

    /// <summary>
    /// Both files are put back to what an older CKli wrote, then the World is opened again: the migration must
    /// convert the versions AND add the import, and the solution must still compile (this is what proves the
    /// migrated state actually restores).
    /// </summary>
    [Test]
    public async Task a_pre_CKliVersion_plugin_solution_is_migrated_Async()
    {
        var (context, plugins) = await CloneAndOpenAsync();

        // Arrange: back to the pre-migration state.
        File.WriteAllText( plugins.AppendPart( "Directory.Build.props" ), PreMigrationDirectoryBuildProps );
        File.WriteAllText( plugins.AppendPart( "Directory.Packages.props" ), PreMigrationDirectoryPackageProps );
        File.Delete( plugins.AppendPart( PluginMachinery.CKliVersionPropsFileName ) );

        // Act: any command that opens the World migrates it.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info" )).ShouldBeTrue();

        // Assert: the version is gone from the tracked file...
        var packages = File.ReadAllText( plugins.AppendPart( "Directory.Packages.props" ) );
        packages.ShouldContain( """<PackageVersion Include="CKli.Plugins.Core" Version="$(CKliVersion)" />""" );
        packages.ShouldNotContain( """Version="0.0.0-0" """.TrimEnd() );

        // ...the import is there, so the property is not empty...
        File.ReadAllText( plugins.AppendPart( "Directory.Build.props" ) )
            .ShouldContain( "CKli.Version.props", Case.Sensitive,
                            "Migrating the versions without adding the import would break every restore." );

        // ...and the generated file has been rewritten.
        File.ReadAllText( plugins.AppendPart( PluginMachinery.CKliVersionPropsFileName ) )
            .ShouldContain( $"<CKliVersion>{World.CKliVersion.Version}</CKliVersion>" );
    }

    /// <summary>
    /// Migrating is idempotent: a second open must not rewrite anything. This is what makes the Stack stop
    /// moving - the property is what every developer writes, whatever CKli version they run.
    /// </summary>
    [Test]
    public async Task migrating_twice_changes_nothing_Async()
    {
        var (context, plugins) = await CloneAndOpenAsync();

        var packagesPath = plugins.AppendPart( "Directory.Packages.props" );
        var buildPath = plugins.AppendPart( "Directory.Build.props" );
        File.WriteAllText( buildPath, PreMigrationDirectoryBuildProps );
        File.WriteAllText( packagesPath, PreMigrationDirectoryPackageProps );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info" )).ShouldBeTrue();
        var packages = File.ReadAllText( packagesPath );
        var build = File.ReadAllText( buildPath );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info" )).ShouldBeTrue();
        File.ReadAllText( packagesPath ).ShouldBe( packages );
        File.ReadAllText( buildPath ).ShouldBe( build );
    }

    static async Task<(CKliEnv Context, NormalizedPath Plugins)> CloneAndOpenAsync(
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null )
    {
        var context = TestEnv.EnsureCleanFolder( name );
        var remotes = TestEnv.OpenRemotes( "One" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri )).ShouldBeTrue();
        context = context.ChangeDirectory( "One" );
        // Opening the World is what creates the plugin solution.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "plugin", "info" )).ShouldBeTrue();
        return (context, context.CurrentDirectory.Combine( ".PublicStack/One-Plugins" ));
    }
}
