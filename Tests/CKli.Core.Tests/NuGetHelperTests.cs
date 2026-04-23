using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
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


    [Test]
    public void NuGetDependencyCache_tests()
    {
        var last = NuGetHelper.Cache.GetAvailableVersions( "ck.TESTING.nunit" ).Max();
        last.ShouldNotBeNull();

        var cache = new NuGetDependencyCache();
        cache.GetRequired( TestHelper.Monitor, "ck.TESTING.nunit", last, out var package ).ShouldBeTrue();

        package.PackageId.ShouldBe( "CK.Testing.NUnit" );
        package.Version.ShouldBe( last );
        package.Dependencies.Select( p => p.PackageId ).ShouldBe( ["CK.Testing.Monitoring", "NUnit"], ignoreOrder: true );
    }
}
