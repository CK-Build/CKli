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
    public async Task Clone_with_diff_casing_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var display = (StringScreen)context.Screen;

        // ckli clone https://github.com/CK-Build/ck-bUILD-stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", "https://github.com/CK-Build/ck-bUILD-stack" )).ShouldBeTrue();

        Directory.EnumerateDirectories( context.CurrentDirectory )
            .Select( path => Path.GetFileName( path ) )
            .ShouldContain( "CK-Build", "The folder name has been fixed." );

        // cd CK-Build
        context = context.ChangeDirectory( "CK-Build" );

        display.Clear();
        // ckli repo list
        ( await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "list" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ··CSemVer-Net···master·↑0↓0·https://github.com/CK-Build/CSemVer-Net·
            ··SGV-Net·······master·↑0↓0·https://github.com/CK-Build/SGV-Net·····
            ··Cake/CodeCake·master·↑0↓0·https://github.com/CK-Build/CodeCake····
            ❰✓❱

            """.Replace( '·', ' ' ) );
    }
}
