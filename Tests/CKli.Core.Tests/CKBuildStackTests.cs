using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class CKBuildStackTests
{
    [Test]
    public async Task Clone_and_DotNet_build_and_test_Async()
    {
        var context = ClonedPaths.EnsureCleanFolder();
        // ckli clone https://github.com/CK-Build/CK-Build-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", "https://github.com/CK-Build/CK-Build-Stack" )).ShouldBeTrue();

        // cd CK-Build
        context = context.ChangeDirectory( "CK-Build" );

        // ckli dotnet build /p:Version=1.1.1
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "dotnet", "build", "/p:Version=1.1.1" )).ShouldBeTrue();

        // ckli dotnet test --no-restore --no-build
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "dotnet", "test", "--no-restore", "--no-build" )).ShouldBeTrue();

    }
}
