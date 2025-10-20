using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class CKBuildStackTests
{
    [Test]
    public async Task Clone_with_diff_casing_and_DotNet_build_and_test_Async()
    {
        var context = ClonedPaths.EnsureCleanFolder();

        // ckli clone https://github.com/CK-Build/ck-bUILD-stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", "https://github.com/CK-Build/ck-bUILD-stack" )).ShouldBeTrue();

        Directory.EnumerateDirectories( context.CurrentDirectory )
            .Select( path => Path.GetFileName( path ) )
            .ShouldContain( "CK-Build", "The folder name has been fixed." );

        // cd CK-Build
        context = context.ChangeDirectory( "CK-Build" );

        // ckli branch
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch" )).ShouldBeTrue();
        ((StringScreen)context.Screen).ToString().ShouldBe( """
             CSemVer-Net master ↑0↓0
             SGV-Net master ↑0↓0
             Cake/CodeCake master ↑0↓0

            """ );

        // ckli dotnet build /p:Version=1.1.1
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "exec", "dotnet", "build", "/p:Version=1.1.1" )).ShouldBeTrue();

        // ckli dotnet test --no-restore --no-build
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "exec", "dotnet", "test", "--no-restore", "--no-build" )).ShouldBeTrue();

    }
}
