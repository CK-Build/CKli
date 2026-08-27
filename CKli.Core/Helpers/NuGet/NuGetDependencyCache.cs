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
/// </summary>
public sealed class NuGetDependencyCache
{
    readonly HashSet<NuGetPackageInstance> _cache;
    readonly HashSet<NuGetPackageInstance>.AlternateLookup<(string, SVersion)> _altLookup;
    readonly List<MissingLink> _missingLinks;
    readonly HashSet<PackageInstance> _missingDeps;

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
    public NuGetDependencyCache()
    {
        _cache = new HashSet<NuGetPackageInstance>( Comp.Instance );
        _altLookup = _cache.GetAlternateLookup<(string, SVersion)>();
        _missingLinks = new List<MissingLink>();
        _missingDeps = new HashSet<PackageInstance>();
    }

    /// <summary>
    /// Gets the missing dependencies details.
    /// </summary>
    public IReadOnlyList<MissingLink> MissingLinks => _missingLinks;

    /// <summary>
    /// Gets the packages that cannot be found in the <see cref="NuGetHelper.Cache"/>.
    /// </summary>
    public IReadOnlySet<PackageInstance> Missing => _missingDeps;

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
            monitor.Error( $"Unable to find '{packageId}@{version}' in NuGet global cache (path: {NuGetHelper.Cache.GetGlobalCachePath( monitor )})." );
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
            packageId = packageId.ToLowerInvariant();
            var path = Path.Combine( NuGetHelper.Cache.GetGlobalCachePath( monitor), packageId, version.ToString(), packageId ) + ".nuspec";
            if( File.Exists( path ) )
            {
                var dependencies = ImmutableArray.CreateBuilder<NuGetPackageInstance>();
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
        return true;

        string? ReadNuspec( IActivityMonitor monitor,
                            string path,
                            XmlReader r,
                            ImmutableArray<NuGetPackageInstance>.Builder dependencies,
                            ref List<(string TargetFramework, NuGetPackageInstance Missing)>? missingDeps )
        {
            if( r.MoveToContent() != XmlNodeType.Element
                || !r.ReadToDescendant( "id" )
                || !r.Read()
                || r.NodeType != XmlNodeType.Text )
            {
                return Error(monitor, "Unable to find <id> element", path, r );
            }
            var actualPackageId = r.Value;
            r.Read();
            Throw.DebugAssert( "We are on the </id>.", r.NodeType == XmlNodeType.EndElement );
            // Not having dependencies is not an error (Microsoft.NETCore.Platforms packages have no dependencies at all).
            if( r.ReadToNextSibling( "dependencies" ) )
            {
                while( r.Read() && r.LocalName == "group" )
                {
                    if( !r.MoveToAttribute( "targetFramework" ) )
                    {
                        return Error( monitor, "Unable to read \"targetFramework\" attribute in <group .../>", path, r );
                    }
                    string targetFramework = r.Value;
                    while( r.Read() && r.LocalName == "dependency" )
                    {
                        if( !ReadIdAndVersion( r, out var id, out var v ) )
                        {
                            return Error( monitor, "Unable to read \"id\" and/or \"version\" attributes in <dependency .../>", path, r );
                        }
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
                        dependencies.Add( dep );
                    }
                }
            }
            return actualPackageId;

            static bool ReadIdAndVersion( XmlReader r, [NotNullWhen( true )] out string? id, [NotNullWhen( true )] out SVersion? v )
            {
                id = null;
                v = null;
                if( r.MoveToFirstAttribute() )
                {
                    do
                    {
                        if( r.LocalName == "id" )
                        {
                            id = r.Value;
                            if( v != null ) return true;
                        }
                        else if( r.LocalName == "version" )
                        {
                            var result = SVersionBound.NugetTryParse( r.Value );
                            if( result.IsValid )
                            {
                                v = result.Result.Base;
                                if( id != null ) return true;
                            }
                        }
                    }
                    while( r.MoveToNextAttribute() );
                }
                return false;
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
