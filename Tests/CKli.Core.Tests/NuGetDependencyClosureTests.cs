using CK.Core;
using CK.Packaging.Abstractions;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[TestFixture]
public class NuGetDependencyClosureTests
{
    [Test]
    public void an_empty_direct_set_gives_an_empty_closure()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( an_empty_direct_set_gives_an_empty_closure ) );
        var c = Create( cache, [], [] );
        c.IsEmpty.ShouldBeTrue();
        c.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// Two paths to the same instance are one member of the closure: nothing is ambiguous here.
    /// </summary>
    [Test]
    public void a_diamond_resolves_linearly()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_diamond_resolves_linearly ),
                                                ("A", "1.0.0", ["net8.0:B@1.0.0", "net8.0:C@1.0.0"]),
                                                ("B", "1.0.0", ["net8.0:D@1.0.0"]),
                                                ("C", "1.0.0", ["net8.0:D@1.0.0"]),
                                                ("D", "1.0.0", []) );
        var c = Create( cache, ["A@1.0.0"], [] );
        Regular( c ).ShouldBe( ["B@1.0.0", "C@1.0.0", "D@1.0.0"] );
        c.Ambiguous.ShouldBeEmpty();
        c.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// Nothing outside the closure anchors D: NuGet's highest-wins resolves it, and every requirement is
    /// reported since each of them disagrees with another.
    /// </summary>
    [Test]
    public void an_identifier_required_at_two_versions_is_resolved_from_its_own_requirements()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( an_identifier_required_at_two_versions_is_resolved_from_its_own_requirements ),
                                                ("A", "1.0.0", ["net8.0:D@1.0.0"]),
                                                ("B", "1.0.0", ["net8.0:D@2.0.0"]),
                                                ("D", "1.0.0", []),
                                                ("D", "2.0.0", []) );
        var c = Create( cache, ["A@1.0.0", "B@1.0.0"], [] );
        c.Regular.ShouldBeEmpty();
        Ambiguous( c ).ShouldBe(
        [
            """
            D@2.0.0 from TransitiveDependencies: 2.0.0 required by [B@1.0.0] for [net8.0] | 1.0.0 required by [A@1.0.0] for [net8.0]
            """
        ] );
        c.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// A repository references D explicitly, so NuGet's nearest-wins makes that version authoritative: the
    /// resolved version is the direct one even though the closure asks for more.
    /// </summary>
    [Test]
    public void a_direct_dependency_anchors_its_identifier()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_direct_dependency_anchors_its_identifier ),
                                                ("A", "1.0.0", ["net8.0:D@2.0.0"]),
                                                ("D", "1.0.0", ["net8.0:E@1.0.0"]),
                                                ("D", "2.0.0", []),
                                                ("E", "1.0.0", []) );
        var c = Create( cache, ["A@1.0.0", "D@1.0.0"], [] );
        // E comes from D@1.0.0, the direct version: the walk follows the roots, not the requirements.
        Regular( c ).ShouldBe( ["E@1.0.0"] );
        Ambiguous( c ).ShouldBe(
        [
            """
            D@1.0.0 from DirectDependencies: 2.0.0 required by [A@1.0.0] for [net8.0]
            """
        ] );
        c.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// The profile produces P itself: a restore gets the produced package, whatever the closure asks for.
    /// </summary>
    [Test]
    public void a_produced_package_anchors_its_identifier()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_produced_package_anchors_its_identifier ),
                                                ("A", "1.0.0", ["net8.0:P@3.0.0"]),
                                                ("P", "3.0.0", []) );
        var c = Create( cache, ["A@1.0.0"], ["P@2.0.0"] );
        c.Regular.ShouldBeEmpty();
        Ambiguous( c ).ShouldBe(
        [
            """
            P@2.0.0 from ProducedPackages: 3.0.0 required by [A@1.0.0] for [net8.0]
            """
        ] );
        c.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// Only the harmful direction is reported: a transitive requirement below - or equal to - what the
    /// profile references or produces is invisible to a restore, and "an identifier is both anchored and
    /// transitively required" is the common case, so reporting it would bury the list.
    /// </summary>
    [TestCase( "1.0.0", TestName = "a_requirement_below_its_anchor_is_dropped" )]
    [TestCase( "2.0.0", TestName = "a_requirement_equal_to_its_anchor_is_dropped" )]
    public void a_harmless_requirement_is_dropped( string required )
    {
        var cache = FakeNuGetCache.CreateGraph( "a_harmless_requirement_is_dropped_" + required,
                                                ("A", "1.0.0", [$"net8.0:D@{required}", $"net8.0:P@{required}"]),
                                                ("D", required, []),
                                                ("D", "2.0.0", []),
                                                ("P", required, []) );
        var c = Create( cache, ["A@1.0.0", "D@2.0.0"], ["P@2.0.0"] );
        // Both identifiers are anchored and neither disagrees: the closure holds nothing at all.
        c.IsEmpty.ShouldBeTrue();
    }

    /// <summary>
    /// A dependency that is absent from the cache is a member of the closure - the requirement exists - but
    /// its own dependencies are unknown, so the closure is incomplete.
    /// </summary>
    [Test]
    public void a_missing_dependency_is_reported()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_missing_dependency_is_reported ),
                                                ("A", "1.0.0", ["net8.0:M@1.0.0"]) );
        var c = Create( cache, ["A@1.0.0"], [] );
        Regular( c ).ShouldBe( ["M@1.0.0"] );
        c.Missing.Select( p => p.ToString() ).ShouldBe( ["M@1.0.0"] );
        c.IsComplete.ShouldBeFalse();
    }

    /// <summary>
    /// A root that is absent from the cache hides its whole sub graph, but it is not a member of the
    /// closure: <see cref="PublishedProfile.DirectDependencies"/> already carries it.
    /// </summary>
    [Test]
    public void a_missing_root_is_reported_and_is_not_a_member_of_the_closure()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_missing_root_is_reported_and_is_not_a_member_of_the_closure ) );
        var c = Create( cache, ["Z@1.0.0"], [] );
        c.Regular.ShouldBeEmpty();
        c.Ambiguous.ShouldBeEmpty();
        c.Missing.Select( p => p.ToString() ).ShouldBe( ["Z@1.0.0"] );
        c.IsComplete.ShouldBeFalse();
    }

    /// <summary>
    /// Completeness is about the RESOLVED versions: a version that loses the resolution is never restored,
    /// so its absence hides nothing. It is still visible - as a requirement of the ambiguity.
    /// </summary>
    [Test]
    public void a_missing_version_that_loses_the_resolution_doesnt_make_the_closure_incomplete()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_missing_version_that_loses_the_resolution_doesnt_make_the_closure_incomplete ),
                                                ("A", "1.0.0", ["net8.0:D@1.0.0"]),
                                                ("B", "1.0.0", ["net8.0:D@2.0.0"]),
                                                ("D", "2.0.0", ["net8.0:E@1.0.0"]),
                                                ("E", "1.0.0", []) );
        var c = Create( cache, ["A@1.0.0", "B@1.0.0"], [] );
        Regular( c ).ShouldBe( ["E@1.0.0"] );
        Ambiguous( c ).ShouldBe(
        [
            """
            D@2.0.0 from TransitiveDependencies: 2.0.0 required by [B@1.0.0] for [net8.0] | 1.0.0 required by [A@1.0.0] for [net8.0]
            """
        ] );
        c.Missing.ShouldBeEmpty();
        c.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// The same holds for an anchored identifier: the anchor is what a restore gets, so a requirement that
    /// cannot be read - the common case for a produced package's other version - hides nothing.
    /// </summary>
    [Test]
    public void a_missing_requirement_of_an_anchored_identifier_doesnt_make_the_closure_incomplete()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_missing_requirement_of_an_anchored_identifier_doesnt_make_the_closure_incomplete ),
                                                ("A", "1.0.0", ["net8.0:P@3.0.0"]) );
        var c = Create( cache, ["A@1.0.0"], ["P@2.0.0"] );
        Ambiguous( c ).ShouldBe(
        [
            """
            P@2.0.0 from ProducedPackages: 3.0.0 required by [A@1.0.0] for [net8.0]
            """
        ] );
        c.Missing.ShouldBeEmpty();
        c.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// The closure's member set is a function of the target framework: it is the selector that decides
    /// which edges are followed, and a package that only a rejected group requires is not in the closure
    /// at all.
    /// </summary>
    [Test]
    public void the_framework_selector_selects_the_edges()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( the_framework_selector_selects_the_edges ),
                                                ("A", "1.0.0", ["net8.0:B@1.0.0", "net9.0:C@1.0.0"]),
                                                ("B", "1.0.0", []),
                                                ("C", "1.0.0", ["net9.0:D@1.0.0"]),
                                                ("D", "1.0.0", []) );
        Regular( Create( cache, ["A@1.0.0"], [] ) ).ShouldBe( ["B@1.0.0", "C@1.0.0", "D@1.0.0"] );

        var net8 = Create( cache,
                           ["A@1.0.0"],
                           [],
                           static ( p, deps ) => [.. deps.Where( d => d.TargetFramework == "net8.0" )] );
        Regular( net8 ).ShouldBe( ["B@1.0.0"] );
    }

    /// <summary>
    /// A requirement's target frameworks are the union across its requesters, so the same instance
    /// required under two frameworks is one requirement carrying both.
    /// </summary>
    [Test]
    public void the_target_frameworks_of_a_requirement_are_the_union_across_its_requesters()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( the_target_frameworks_of_a_requirement_are_the_union_across_its_requesters ),
                                                ("A", "1.0.0", ["net8.0:D@1.0.0", "net9.0:D@1.0.0"]),
                                                ("B", "1.0.0", ["net9.0:D@1.0.0"]),
                                                ("C", "1.0.0", ["net8.0:D@2.0.0"]),
                                                ("D", "1.0.0", []),
                                                ("D", "2.0.0", []) );
        var c = Create( cache, ["A@1.0.0", "B@1.0.0", "C@1.0.0"], [] );
        Ambiguous( c ).ShouldBe(
        [
            """
            D@2.0.0 from TransitiveDependencies: 2.0.0 required by [C@1.0.0] for [net8.0] | 1.0.0 required by [A@1.0.0, B@1.0.0] for [net8.0, net9.0]
            """
        ] );
    }

    /// <summary>
    /// A cycle is not a valid NuGet graph, but a hand written nuspec can hold one and the cache reports it
    /// as an error: the closure fails rather than answering a truncated one.
    /// </summary>
    [Test]
    public void a_cache_error_fails_the_closure()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_cache_error_fails_the_closure ),
                                                ("A", "1.0.0", ["net8.0:B@1.0.0"]),
                                                ("B", "1.0.0", ["net8.0:A@1.0.0"]) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            NuGetDependencyClosure.Create( TestHelper.Monitor, cache, [Instance( "A@1.0.0" )], [] )
                                  .ShouldBeNull();
            logs.ShouldContain( "Dependency cycle detected on 'A@1.0.0' while reading the nuspec files." );
        }
    }

    [Test]
    public void an_incoherent_direct_set_is_an_error()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( an_incoherent_direct_set_is_an_error ),
                                                ("D", "1.0.0", []),
                                                ("D", "2.0.0", []) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            NuGetDependencyClosure.Create( TestHelper.Monitor,
                                           cache,
                                           [Instance( "D@1.0.0" ), Instance( "d@2.0.0" )],
                                           [] )
                                  .ShouldBeNull();
            logs.ShouldContain( "Incoherent direct dependency 'd': it appears as '1.0.0' and '2.0.0'. "
                                + "One identifier can only be at one version." );
        }
    }

    /// <summary>
    /// The direct dependencies are the consumed packages MINUS the produced ones: an identifier in both
    /// sets means the caller computed them wrong, and it would silently decide which anchor wins.
    /// </summary>
    [Test]
    public void a_direct_dependency_that_is_also_produced_is_an_error()
    {
        var cache = FakeNuGetCache.CreateGraph( nameof( a_direct_dependency_that_is_also_produced_is_an_error ),
                                                ("P", "1.0.0", []) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            NuGetDependencyClosure.Create( TestHelper.Monitor, cache, [Instance( "P@1.0.0" )], [Instance( "P@1.0.0" )] )
                                  .ShouldBeNull();
            logs.ShouldContain( "Direct dependency 'P@1.0.0' is also produced ('P@1.0.0'): the direct "
                                + "dependencies are the consumed packages minus the produced ones." );
        }
    }

    static TransitiveDependencies Create( NuGetDependencyCache cache,
                                          string[] directDependencies,
                                          string[] producedPackages,
                                          NuGetDependencyClosure.FrameworkSelector? frameworkSelector = null )
    {
        var c = NuGetDependencyClosure.Create( TestHelper.Monitor,
                                               cache,
                                               directDependencies.Select( Instance ),
                                               producedPackages.Select( Instance ),
                                               frameworkSelector );
        c.ShouldNotBeNull();
        return c;
    }

    static PackageInstance Instance( string instance )
    {
        PackageInstance.TryParse( instance, out var p ).ShouldBeTrue();
        return p!;
    }

    static string[] Regular( TransitiveDependencies c ) => c.Regular.Select( p => p.ToString() ).ToArray();

    static string[] Ambiguous( TransitiveDependencies c )
    {
        return c.Ambiguous
                .Select( a => $"{a} from {a.ResolvedFrom}: "
                              + a.Requirements.Select( r => r.ToString() ).Concatenate( " | " ) )
                .ToArray();
    }
}
