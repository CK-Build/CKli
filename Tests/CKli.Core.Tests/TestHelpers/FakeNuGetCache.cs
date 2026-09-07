using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

/// <summary>
/// Creates a <see cref="NuGetDependencyCache"/> over a temporary folder holding one nuspec per package,
/// in the NuGet global cache's layout: "&lt;package id lower invariant&gt;/&lt;version&gt;/&lt;package id
/// lower invariant&gt;.nuspec".
/// </summary>
static class FakeNuGetCache
{
    /// <summary>
    /// Creates a cache from raw &lt;dependencies&gt; blocks: this is what the nuspec reader's own tests
    /// use, so that the Xml shape under test is visible in the test.
    /// </summary>
    /// <param name="testName">The test name. Names the temporary folder.</param>
    /// <param name="packages">
    /// The packages. The Dependencies is the whole &lt;dependencies&gt; element (or the empty string when
    /// the package has none).
    /// </param>
    /// <returns>The cache.</returns>
    public static NuGetDependencyCache Create( string testName,
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

    /// <summary>
    /// Creates a cache from a graph description: this is what the closure's tests use, since their subject
    /// is the shape of the graph and not the shape of the Xml.
    /// </summary>
    /// <param name="testName">The test name. Names the temporary folder.</param>
    /// <param name="packages">
    /// The packages. Each dependency is "&lt;targetFramework&gt;:&lt;id&gt;@&lt;version&gt;", or
    /// "&lt;id&gt;@&lt;version&gt;" for a dependency that applies to any framework. Dependencies of the
    /// same target framework are emitted in one &lt;group&gt;.
    /// </param>
    /// <returns>The cache.</returns>
    public static NuGetDependencyCache CreateGraph( string testName,
                                                    params (string PackageId, string Version, string[] Dependencies)[] packages )
    {
        var withXml = new (string, string, string)[packages.Length];
        for( int i = 0; i < packages.Length; ++i )
        {
            var (packageId, version, dependencies) = packages[i];
            withXml[i] = (packageId, version, GetDependenciesXml( dependencies ));
        }
        return Create( testName, withXml );
    }

    static string GetDependenciesXml( string[] dependencies )
    {
        if( dependencies.Length == 0 ) return "";
        // Preserves the declaration order of the frameworks and of the dependencies inside each of them:
        // the reader's output order is what the tests describe.
        var groups = new List<(string TargetFramework, List<string> Dependencies)>();
        foreach( var d in dependencies )
        {
            int idx = d.IndexOf( ':' );
            var targetFramework = idx < 0 ? "" : d.Substring( 0, idx );
            var instance = idx < 0 ? d : d.Substring( idx + 1 );
            if( !PackageInstance.TryParse( instance, out var p ) )
            {
                Throw.ArgumentException( nameof( dependencies ), $"Invalid dependency '{d}'." );
            }
            var group = groups.Find( g => g.TargetFramework == targetFramework );
            if( group.Dependencies == null )
            {
                group = (targetFramework, new List<string>());
                groups.Add( group );
            }
            group.Dependencies.Add( $"""<dependency id="{p.PackageId}" version="{p.Version}" />""" );
        }
        var b = new StringBuilder();
        b.Append( "    <dependencies>" ).Append( Environment.NewLine );
        foreach( var (targetFramework, deps) in groups )
        {
            // A <group> without a "targetFramework" attribute applies to any framework.
            b.Append( targetFramework.Length == 0
                        ? "      <group>"
                        : $"""      <group targetFramework="{targetFramework}">""" )
             .Append( Environment.NewLine );
            foreach( var d in deps )
            {
                b.Append( "        " ).Append( d ).Append( Environment.NewLine );
            }
            b.Append( "      </group>" ).Append( Environment.NewLine );
        }
        b.Append( "    </dependencies>" );
        return b.ToString();
    }
}
