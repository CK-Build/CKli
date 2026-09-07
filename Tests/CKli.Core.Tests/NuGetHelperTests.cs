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
        var last = NuGetHelper.Cache.GetAvailableVersions( TestHelper.Monitor, "ck.TESTING.nunit" ).Max();
        last.ShouldNotBeNull();

        var cache = new NuGetDependencyCache();
        cache.GetRequired( TestHelper.Monitor, "ck.TESTING.nunit", last, out var package ).ShouldBeTrue();

        package.PackageId.ShouldBe( "CK.Testing.NUnit" );
        package.Version.ShouldBe( last );
        // Distinct(): a package identifier appears once per dependency group that requires it.
        package.Dependencies.Select( d => d.Package.PackageId )
                            .Distinct()
                            .ShouldBe( ["CK.Testing.Monitoring", "NUnit"], ignoreOrder: true );
        // Whichever version happens to be cached here, a modern package has no flat dependency list:
        // every dependency comes from a framework qualified <group>.
        package.Dependencies.ShouldAllBe( d => d.TargetFramework.Length > 0 );
    }

    [Test]
    public void dependency_groups_are_read_with_their_target_framework()
    {
        const string deps = """
              <dependencies>
                <group targetFramework="net8.0">
                  <dependency id="B" version="1.0.0" />
                </group>
                <group targetFramework="net9.0">
                  <dependency id="C" version="2.0.0" />
                </group>
              </dependencies>
            """;
        var cache = CreateCache( nameof( dependency_groups_are_read_with_their_target_framework ),
                                 ("A", "1.0.0", deps),
                                 ("B", "1.0.0", ""),
                                 ("C", "2.0.0", "") );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        Describe( a ).ShouldBe( ["net8.0:B@1.0.0", "net9.0:C@2.0.0"] );
    }

    /// <summary>
    /// The regression that made the whole cache unusable: a self closed &lt;group/&gt; - which is very
    /// common, System.Text.Json has one - used to make the reader skip the next &lt;dependency&gt; AND
    /// leave the group loop, silently dropping every remaining dependency of the file.
    /// </summary>
    [Test]
    public void a_self_closed_empty_group_doesnt_hide_the_following_groups()
    {
        const string deps = """
              <dependencies>
                <group targetFramework="net8.0">
                  <dependency id="B" version="1.0.0" />
                </group>
                <group targetFramework="net9.0" />
                <group targetFramework=".NETStandard2.0">
                  <dependency id="C" version="2.0.0" />
                </group>
              </dependencies>
            """;
        var cache = CreateCache( nameof( a_self_closed_empty_group_doesnt_hide_the_following_groups ),
                                 ("A", "1.0.0", deps),
                                 ("B", "1.0.0", ""),
                                 ("C", "2.0.0", "") );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        Describe( a ).ShouldBe( ["net8.0:B@1.0.0", ".NETStandard2.0:C@2.0.0"] );
    }

    [Test]
    public void an_empty_group_pair_is_skipped()
    {
        const string deps = """
              <dependencies>
                <group targetFramework="net8.0"></group>
                <group targetFramework="net9.0">
                  <dependency id="B" version="1.0.0" />
                </group>
              </dependencies>
            """;
        var cache = CreateCache( nameof( an_empty_group_pair_is_skipped ),
                                 ("A", "1.0.0", deps),
                                 ("B", "1.0.0", "") );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        Describe( a ).ShouldBe( ["net9.0:B@1.0.0"] );
    }

    /// <summary>
    /// The legacy, group less form. It used to be read as no dependency at all.
    /// </summary>
    [Test]
    public void a_flat_dependency_list_is_read_with_no_target_framework()
    {
        const string deps = """
              <dependencies>
                <dependency id="B" version="1.0.0" />
                <dependency id="C" version="2.0.0" />
              </dependencies>
            """;
        var cache = CreateCache( nameof( a_flat_dependency_list_is_read_with_no_target_framework ),
                                 ("A", "1.0.0", deps),
                                 ("B", "1.0.0", ""),
                                 ("C", "2.0.0", "") );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        Describe( a ).ShouldBe( [":B@1.0.0", ":C@2.0.0"] );
    }

    /// <summary>
    /// A &lt;group&gt; with no "targetFramework" attribute is legal and applies to any framework: the
    /// empty string stands for it, exactly like a flat dependency. This used to be an error.
    /// </summary>
    [Test]
    public void a_group_without_targetFramework_applies_to_any_framework()
    {
        const string deps = """
              <dependencies>
                <group>
                  <dependency id="B" version="1.0.0" />
                </group>
              </dependencies>
            """;
        var cache = CreateCache( nameof( a_group_without_targetFramework_applies_to_any_framework ),
                                 ("A", "1.0.0", deps),
                                 ("B", "1.0.0", "") );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        Describe( a ).ShouldBe( [":B@1.0.0"] );
    }

    /// <summary>
    /// The target framework is part of the edge's identity: a duplicate inside one group is one edge,
    /// but two groups requiring the same instance are two.
    /// </summary>
    [Test]
    public void duplicate_dependencies_of_a_group_are_read_once()
    {
        const string deps = """
              <dependencies>
                <group targetFramework="net8.0">
                  <dependency id="B" version="1.0.0" />
                  <dependency id="B" version="1.0.0" />
                </group>
                <group targetFramework="net9.0">
                  <dependency id="B" version="1.0.0" />
                </group>
              </dependencies>
            """;
        var cache = CreateCache( nameof( duplicate_dependencies_of_a_group_are_read_once ),
                                 ("A", "1.0.0", deps),
                                 ("B", "1.0.0", "") );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        Describe( a ).ShouldBe( ["net8.0:B@1.0.0", "net9.0:B@1.0.0"] );
    }

    [Test]
    public void a_version_range_is_reduced_to_its_base_version()
    {
        const string deps = """
              <dependencies>
                <group targetFramework="net8.0">
                  <dependency id="B" version="[1.0.0,2.0.0)" />
                </group>
              </dependencies>
            """;
        var cache = CreateCache( nameof( a_version_range_is_reduced_to_its_base_version ),
                                 ("A", "1.0.0", deps),
                                 ("B", "1.0.0", "") );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        Describe( a ).ShouldBe( ["net8.0:B@1.0.0"] );
    }

    [Test]
    public void no_dependencies_at_all_is_not_an_error()
    {
        var cache = CreateCache( nameof( no_dependencies_at_all_is_not_an_error ), ("A", "1.0.0", "") );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        a.Dependencies.ShouldBeEmpty();
    }

    /// <summary>
    /// A dependency that is not in the cache is kept as an edge - the requirement exists - but its own
    /// dependencies are unknown, so it is tracked by <see cref="NuGetDependencyCache.Missing"/> and
    /// <see cref="NuGetDependencyCache.MissingLinks"/>.
    /// </summary>
    [Test]
    public void a_dependency_that_is_not_in_the_cache_is_tracked()
    {
        const string deps = """
              <dependencies>
                <group targetFramework="net8.0">
                  <dependency id="B" version="1.0.0" />
                </group>
              </dependencies>
            """;
        var cache = CreateCache( nameof( a_dependency_that_is_not_in_the_cache_is_tracked ), ("A", "1.0.0", deps) );
        cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out var a ).ShouldBeTrue();
        Describe( a ).ShouldBe( ["net8.0:B@1.0.0"] );
        cache.Missing.Select( p => p.ToString() ).ShouldBe( ["B@1.0.0"] );
        cache.MissingLinks.Select( l => $"{l.From}-{l.TargetFramework}->{l.Missing}" )
                          .ShouldBe( ["A@1.0.0-net8.0->B@1.0.0"] );
    }

    /// <summary>
    /// A package is added to the cache only once its dependencies have been read, so a cycle - which no
    /// valid NuGet graph has, but a hand written nuspec can - would recurse until the stack overflows.
    /// </summary>
    [Test]
    public void a_dependency_cycle_is_an_error()
    {
        const string toB = """
              <dependencies>
                <group targetFramework="net8.0">
                  <dependency id="B" version="1.0.0" />
                </group>
              </dependencies>
            """;
        const string toA = """
              <dependencies>
                <group targetFramework="net8.0">
                  <dependency id="A" version="1.0.0" />
                </group>
              </dependencies>
            """;
        var cache = CreateCache( nameof( a_dependency_cycle_is_an_error ),
                                 ("A", "1.0.0", toB),
                                 ("B", "1.0.0", toA) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            cache.GetRequired( TestHelper.Monitor, "A", SVersion.Parse( "1.0.0" ), out _ ).ShouldBeFalse();
            logs.ShouldContain( "Dependency cycle detected on 'A@1.0.0' while reading the nuspec files." );
        }
    }

    static string[] Describe( NuGetPackageInstance p )
    {
        return p.Dependencies.Select( d => $"{d.TargetFramework}:{d.Package}" ).ToArray();
    }

    // Creates a cache over a folder holding one nuspec per package, in the NuGet global cache's layout:
    // "<package id lower invariant>/<version>/<package id lower invariant>.nuspec".
    static NuGetDependencyCache CreateCache( string testName,
                                             params (string PackageId, string Version, string Dependencies)[] packages )
    {
        var root = Path.Combine( Path.GetTempPath(), "CKliNuGetDependencyCacheTests", testName );
        TestHelper.CleanupFolder( root, ensureFolderAvailable: true );
        foreach( var (packageId, version, dependencies) in packages )
        {
            var id = packageId.ToLowerInvariant();
            var folder = Path.Combine( root, id, version );
            Directory.CreateDirectory( folder );
            File.WriteAllText( Path.Combine( folder, id + ".nuspec" ),
                               $"""
                                <?xml version="1.0" encoding="utf-8"?>
                                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                                  <metadata>
                                    <id>{packageId}</id>
                                    <version>{version}</version>
                                {dependencies}
                                  </metadata>
                                </package>
                                """ );
        }
        return new NuGetDependencyCache( root );
    }
}
