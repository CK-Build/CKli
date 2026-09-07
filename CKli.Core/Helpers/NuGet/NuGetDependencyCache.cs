using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml;

namespace CKli.Core;

/// <summary>
/// Simple NuGet package dependency graph implementation that relies on the <see cref="NuGetHelper.Cache"/> to obtain
/// the dependencies by reading the Xml nuspec files.
/// The version ranges are read by <see cref="SVersionBound.NugetTryParse(ReadOnlySpan{char})"/> and only the <see cref="SVersionBound.Base"/>
/// version is considered.
/// <para>
/// The dependencies are NOT filtered by target framework: every group of a nuspec is read and each
/// <see cref="NuGetPackageInstance.Dependency"/> carries the framework it comes from. Resolving a graph for a
/// given framework is the caller's business.
/// </para>
/// </summary>
public sealed class NuGetDependencyCache
{
    readonly HashSet<NuGetPackageInstance> _cache;
    readonly HashSet<NuGetPackageInstance>.AlternateLookup<(string, SVersion)> _altLookup;
    readonly List<MissingLink> _missingLinks;
    readonly HashSet<PackageInstance> _missingDeps;
    // A package is added to _cache only once its dependencies have been read, so a cycle would recurse
    // forever: this tracks the reads that are in progress (the package identifier is lower invariant).
    readonly HashSet<(string PackageId, SVersion Version)> _reading;
    readonly string? _cachePath;

    static readonly XmlReaderSettings _readerSettings = new XmlReaderSettings
    {
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true
    };

    sealed class Comp : IEqualityComparer<NuGetPackageInstance>, IAlternateEqualityComparer<(string, SVersion), NuGetPackageInstance>
    {
        public readonly static Comp Instance = new Comp();

        public bool Equals( NuGetPackageInstance? x, NuGetPackageInstance? y ) => x == y;

        public int GetHashCode( [DisallowNull] NuGetPackageInstance obj ) => obj.GetHashCode();

        public int GetHashCode( (string, SVersion) a ) => HashCode.Combine( StringComparer.OrdinalIgnoreCase.GetHashCode( a.Item1 ), a.Item2.GetHashCode() );

        public bool Equals( (string, SVersion) a, NuGetPackageInstance o ) => StringComparer.OrdinalIgnoreCase.Equals( o.PackageId, a.Item1 ) && o.Version == a.Item2;

        public NuGetPackageInstance Create( (string, SVersion) alternate ) => throw new System.NotSupportedException();

    }

    /// <summary>
    /// Captures a missing dependency between <see cref="From"/> and <see cref="Missing"/>.
    /// </summary>
    /// <param name="From">The source of the reference.</param>
    /// <param name="TargetFramework">The target framework of the dependency.</param>
    /// <param name="Missing">The missing package. It appears in the From's <see cref="NuGetPackageInstance.Dependencies"/>.</param>
    public sealed record MissingLink( NuGetPackageInstance From, string TargetFramework, NuGetPackageInstance Missing );

    /// <summary>
    /// Initializes a new empty cache.
    /// </summary>
    /// <param name="cachePath">
    /// Optional folder to read the nuspec files from instead of the <see cref="NuGetHelper.Cache.GetGlobalCachePath(IActivityMonitor)"/>.
    /// The expected layout is the global cache's one: "&lt;cachePath&gt;/&lt;package id lower invariant&gt;/&lt;version&gt;/&lt;package id lower invariant&gt;.nuspec".
    /// </param>
    public NuGetDependencyCache( string? cachePath = null )
    {
        _cache = new HashSet<NuGetPackageInstance>( Comp.Instance );
        _altLookup = _cache.GetAlternateLookup<(string, SVersion)>();
        _missingLinks = new List<MissingLink>();
        _missingDeps = new HashSet<PackageInstance>();
        _reading = new HashSet<(string, SVersion)>();
        _cachePath = cachePath;
    }

    /// <summary>
    /// Gets the missing dependencies details.
    /// </summary>
    public IReadOnlyList<MissingLink> MissingLinks => _missingLinks;

    /// <summary>
    /// Gets the packages that cannot be found in the <see cref="GetCachePath(IActivityMonitor)"/> folder.
    /// </summary>
    public IReadOnlySet<PackageInstance> Missing => _missingDeps;

    /// <summary>
    /// Gets the folder the nuspec files are read from: the <c>cachePath</c> of the constructor when one has
    /// been provided, the <see cref="NuGetHelper.Cache.GetGlobalCachePath(IActivityMonitor)"/> otherwise.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The folder path.</returns>
    public string GetCachePath( IActivityMonitor monitor ) => _cachePath ?? NuGetHelper.Cache.GetGlobalCachePath( monitor );

    /// <summary>
    /// Gets a <see cref="NuGetPackageInstance"/> or returns false and logs an error.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="packageId">The package identifier. Lookup is case insensitive.</param>
    /// <param name="version">The package version.</param>
    /// <param name="package">On success, the package instance with its dependencies.</param>
    /// <returns>True on success, false on error (and if the package cannot be found).</returns>
    public bool GetRequired( IActivityMonitor monitor,
                             string packageId,
                             SVersion version,
                             [NotNullWhen( true )] out NuGetPackageInstance? package )
    {
        if( Get( monitor, packageId, version, out package ) )
        {
            if( package != null ) return true;
            monitor.Error( $"Unable to find '{packageId}@{version}' in NuGet global cache (path: {GetCachePath( monitor )})." );
        }
        return false;
    }

    /// <summary>
    /// Gets a <see cref="NuGetPackageInstance"/> if it exists.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="packageId">The package identifier. Lookup is case insensitive.</param>
    /// <param name="version">The package version.</param>
    /// <param name="package">On success and if found, the package instance with its dependencies. Null otherwise.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Get( IActivityMonitor monitor, string packageId, SVersion version, out NuGetPackageInstance? package )
    {
        if( !_altLookup.TryGetValue( (packageId, version), out package ) )
        {
            var id = packageId.ToLowerInvariant();
            if( !_reading.Add( (id, version) ) )
            {
                monitor.Error( $"Dependency cycle detected on '{packageId}@{version}' while reading the nuspec files." );
                return false;
            }
            try
            {
                var path = Path.Combine( GetCachePath( monitor ), id, version.ToString(), id ) + ".nuspec";
                if( File.Exists( path ) )
                {
                    var dependencies = ImmutableArray.CreateBuilder<NuGetPackageInstance.Dependency>();
                    List<(string TargetFramework, NuGetPackageInstance Missing)>? missingDeps = null;
                    string? actualPackageId;
                    using( var f = File.OpenRead( path ) )
                    using( var r = XmlReader.Create( f, _readerSettings ) )
                    {
                        actualPackageId = ReadNuspec( monitor, path, r, dependencies, ref missingDeps );
                        if( actualPackageId == null )
                        {
                            return false;
                        }
                    }
                    package = new NuGetPackageInstance( actualPackageId, version, dependencies.DrainToImmutable() );
                    if( missingDeps != null )
                    {
                        foreach( var dep in missingDeps )
                        {
                            _missingLinks.Add( new MissingLink( package, dep.TargetFramework, dep.Missing ) );
                        }
                    }
                    _cache.Add( package );
                }
            }
            finally
            {
                _reading.Remove( (id, version) );
            }
        }
        return true;

        string? ReadNuspec( IActivityMonitor monitor,
                            string path,
                            XmlReader r,
                            ImmutableArray<NuGetPackageInstance.Dependency>.Builder dependencies,
                            ref List<(string TargetFramework, NuGetPackageInstance Missing)>? missingDeps )
        {
            if( r.MoveToContent() != XmlNodeType.Element
                || !r.ReadToDescendant( "id" )
                || !r.Read()
                || r.NodeType != XmlNodeType.Text )
            {
                return Error( monitor, "Unable to find <id> element", path, r );
            }
            var actualPackageId = r.Value;
            r.Read();
            Throw.DebugAssert( "We are on the </id>.", r.NodeType == XmlNodeType.EndElement );
            // Not having dependencies is not an error (Microsoft.NETCore.Platforms packages have no dependencies at all).
            if( !r.ReadToNextSibling( "dependencies" ) )
            {
                return actualPackageId;
            }
            // The nuspec schema allows the <dependency> elements to be listed flat or grouped by target framework,
            // and a <group> can perfectly be empty - self closed in particular, which is very common (System.Text.Json
            // has one). Reading the subtree and dispatching on the element name handles every form, and any mix of
            // them, instead of relying on the reader landing exactly where a nested loop expects it: a self closed
            // <group/> used to make the reader skip the following <dependency> AND leave the outer loop, silently
            // dropping every remaining dependency of the file.
            using( var sub = r.ReadSubtree() )
            {
                if( !sub.Read() )
                {
                    return actualPackageId;
                }
                Throw.DebugAssert( "The subtree starts on the <dependencies> element.",
                                   sub.NodeType == XmlNodeType.Element && sub.LocalName == "dependencies" );
                // The depth is captured rather than assumed: only its relative value matters here.
                int rootDepth = sub.Depth;
                // A malformed nuspec can list the same dependency twice in one group. Two groups requiring the
                // same instance are 2 legitimate edges: the target framework is part of the key.
                var seen = new HashSet<(string TargetFramework, string PackageId, SVersion Version)>();
                var groupFramework = "";
                while( sub.Read() )
                {
                    if( sub.NodeType != XmlNodeType.Element ) continue;
                    if( sub.LocalName == "group" )
                    {
                        // A <group> without a "targetFramework" attribute applies to any framework: the empty
                        // string stands for it, exactly like a flat <dependency>.
                        groupFramework = sub.GetAttribute( "targetFramework" ) ?? "";
                        continue;
                    }
                    if( sub.LocalName != "dependency" ) continue;
                    // A <dependency> directly under <dependencies> is a flat one: no group, hence no framework.
                    var targetFramework = sub.Depth == rootDepth + 1 ? "" : groupFramework;
                    if( !ReadIdAndVersion( sub, out var id, out var v ) )
                    {
                        return Error( monitor, "Unable to read \"id\" and/or \"version\" attributes in <dependency .../>", path, sub );
                    }
                    if( !seen.Add( (targetFramework, id, v) ) ) continue;
                    if( !Get( monitor, id, v, out var dep ) )
                    {
                        return null;
                    }
                    bool isMissing = false;
                    if( dep == null )
                    {
                        dep = new NuGetPackageInstance( id, v, [] );
                        isMissing = true;
                        _missingDeps.Add( dep );
                    }
                    else if( _missingDeps.Contains( dep ) )
                    {
                        isMissing = true;
                    }
                    if( isMissing )
                    {
                        missingDeps ??= new List<(string TargetFramework, NuGetPackageInstance Missing)>();
                        missingDeps.Add( (targetFramework, dep) );
                    }
                    dependencies.Add( new NuGetPackageInstance.Dependency( targetFramework, dep ) );
                }
            }
            return actualPackageId;

            // GetAttribute is used rather than MoveToFirstAttribute/MoveToNextAttribute: it leaves the reader on
            // the element, so the enclosing loop's next Read() and the Depth test above stay predictable.
            static bool ReadIdAndVersion( XmlReader r, [NotNullWhen( true )] out string? id, [NotNullWhen( true )] out SVersion? v )
            {
                v = null;
                id = r.GetAttribute( "id" );
                if( string.IsNullOrWhiteSpace( id ) )
                {
                    id = null;
                    return false;
                }
                var version = r.GetAttribute( "version" );
                if( version == null ) return false;
                var result = SVersionBound.NugetTryParse( version );
                if( !result.IsValid ) return false;
                v = result.Result.Base;
                return true;
            }

            static string? Error( IActivityMonitor monitor, string message, string path, XmlReader r )
            {
                string position = r is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
                                    ? $"{lineInfo.LineNumber},{lineInfo.LinePosition}"
                                    : "unknown";
                monitor.Error( $"""
                               {message} in file {path}:
                               {File.ReadAllText( path )}
                               @({position})
                               """ );
                return null;
            }
        }
    }
}
