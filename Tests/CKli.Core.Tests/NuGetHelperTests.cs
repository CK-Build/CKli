using NUnit.Framework;
using Shouldly;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class NuGetHelperTests
{
    [Test]
    public void creating_V3_local_feed_and_pushing()
    {
        var tempFolder = Path.Combine( Path.GetTempPath(), "creating_V3_local_feed_and_pushing" );
        try
        {
            NuGetHelper.EnsureLocalFeed( TestHelper.Monitor, tempFolder ).ShouldBeTrue();
            var nupkgFilePath = TestEnv.EnsurePluginPackage( "CKli.CommandSample.Plugin" );
            NuGetHelper.PushToLocalFeed( TestHelper.Monitor, nupkgFilePath, tempFolder );

            var packageIdFolder = Path.Combine( tempFolder, "CKli.CommandSample.Plugin" );
            Directory.Exists( packageIdFolder ).ShouldBeTrue();
            var versionedFolder = Path.Combine( packageIdFolder, TestEnv.CKliPluginsCoreVersion.ToString() );
            Directory.Exists( versionedFolder ).ShouldBeTrue();
            var signaturePath = Path.Combine( versionedFolder, $"ckli.commandsample.plugin.{TestEnv.CKliPluginsCoreVersion}.nupkg.sha512" );
            File.Exists( signaturePath ).ShouldBeTrue();
        }
        finally
        {
            TestHelper.CleanupFolder( tempFolder, ensureFolderAvailable: false );
        }
    }
}
