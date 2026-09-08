using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Read only model of a solution as a consumer/producer of packages.
/// <para>
/// This only exposes the <see cref="Projects"/> and the <see cref="Consumed"/> packages.
/// </para>
/// </summary>
public partial class GitSolutionContent
{
    readonly HashSet<PackageInstance> _consumed;
    readonly List<Project> _projects;
    // The version first met for each package identifier and the file that carries it: this is what
    // rejects a solution referencing one identifier in two versions. Only used while loading.
    readonly Dictionary<string, (SVersion Version, NormalizedPath Path)> _firstMet;

    /// <summary>
    /// Gets the projects.
    /// </summary>
    public IReadOnlyList<Project> Projects => _projects;

    /// <summary>
    /// Gets all the &lt;PackageReference ... /&gt; that have been found in <see cref="Projects"/> files
    /// and all "Directory.Build.props" (reachable from Projects).
    /// </summary>
    public IReadOnlySet<PackageInstance> Consumed => _consumed;

    /// <summary>
    /// Gets whether the solution has at least one dependency that must be updated considering the <paramref name="mapping"/>. 
    /// </summary>
    /// <param name="mapping">The package mapping to consider.</param>
    /// <returns>True if the dependencies should be updated, false otherwise.</returns>
    public bool HasUpdates( IPackageMapping mapping ) => _consumed.Any( p => mapping.TryGetMappedVersion( p.PackageId, p.Version, out var version ) && p.Version != version );

    /// <summary>
    /// Gets whether the solution has at least one dependency that must be updated considering the <paramref name="mapping"/> and collects these updates.
    /// </summary>
    /// <param name="mapping">The package mapping to consider.</param>
    /// <param name="updates">On success, the non null updates that must be done.</param>
    /// <returns>True if the dependencies should be updated, false otherwise.</returns>
    public bool HasUpdates( IPackageMapping mapping, [NotNullWhen( true )] ref PackageMapper? updates )
    {
        bool hasUpdates = false;
        foreach( var p in _consumed )
        {
            if( mapping.TryGetMappedVersion( p.PackageId, p.Version, out var version ) && p.Version != version )
            {
                updates ??= new PackageMapper();
                updates.Add( p.PackageId, p.Version, version );
                hasUpdates = true;
            }
        }
        return hasUpdates;
    }

    /// <summary>
    /// Gets whether the solution has at least one dependency that must be updated considering any number of <paramref name="mappings"/>
    /// and collects these updates.
    /// </summary>
    /// <param name="updates">Called for each update that must be made. The last parameter is the index of the mapping in <paramref name="mappings"/>.</param>
    /// <param name="mappings">The package mappings to consider in priority order. Some may be null: they are ignored but their index matters.</param>
    /// <returns>True if the dependencies should be updated, false otherwise.</returns>
    public bool HasUpdates( Action<PackageInstance, SVersion, int> updates, params ReadOnlySpan<IPackageMapping?> mappings )
    {
        bool hasUpdates = false;
        foreach( var p in _consumed )
        {
            for( int i = 0; i < mappings.Length; ++i )
            {
                var m = mappings[i];
                if( m != null && m.TryGetMappedVersion( p.PackageId, p.Version, out var version ) && p.Version != version )
                {
                    hasUpdates = true;
                    updates( p, version, i );
                    break;
                }
            }
        }
        return hasUpdates;
    }

    private protected GitSolutionContent()
    {
        _consumed = new HashSet<PackageInstance>();
        _projects = new List<Project>();
        _firstMet = new Dictionary<string, (SVersion, NormalizedPath)>( StringComparer.OrdinalIgnoreCase );
    }

    internal static GitSolutionContent? Create( IActivityMonitor monitor, INormalizedFileProvider files, XDocument doc )
    {
        var s = new GitSolutionContent();
        return s.Initialize( monitor, files, doc ) ? s : null;
    }

    internal bool Initialize( IActivityMonitor monitor, INormalizedFileProvider files, XDocument doc )
    {
        return CommonSolution.LoadAllProjectFiles( monitor,
                                                   files,
                                                   doc.Root!,
                                                   LoadOptions.None,
                                                   AddProjectFile );
    }

    bool AddProjectFile( IActivityMonitor monitor, NormalizedPath path, XElement project )
    {
        if( path.LastPart.EndsWith( ".csproj", StringComparison.OrdinalIgnoreCase ) )
        {
            _projects.Add( new Project( path, project ) );
            if( !HandlePackageReferences( monitor, path, project ) )
            {
                return false;
            }
        }
        else if( path.LastPart.Equals( "Directory.Build.props", StringComparison.OrdinalIgnoreCase ) )
        {
            if( !HandlePackageReferences( monitor, path, project ) )
            {
                return false;
            }
        }
        else
        {
            Throw.DebugAssert( path.LastPart.Equals( "Directory.Packages.props", StringComparison.OrdinalIgnoreCase ) );
            foreach( var e in project.Descendants( XNames.PackageVersion ) )
            {
                var packageId = CommonSolution.GetIncludedName( monitor, path, e, CK.Core.LogLevel.Error );
                if( packageId == null
                    || !CommonSolution.ReadVersionAttribute( monitor, path, e, XNames.Version, "Version", out var _, out var version ) )
                {
                    return false;
                }
                Throw.DebugAssert( version != null );
                if( !AddConsumed( monitor, path, packageId, version ) )
                {
                    return false;
                }
            }
        }
        return true;

        bool HandlePackageReferences( IActivityMonitor monitor, NormalizedPath path, XElement project )
        {
            foreach( var e in project.Descendants( XNames.PackageReference ) )
            {
                var packageId = CommonSolution.GetIncludedName( monitor, path, e, CK.Core.LogLevel.Error );
                if( packageId == null )
                {
                    return false;
                }
                if( !CommonSolution.ReadVersionAttribute( monitor, path, e, XNames.VersionOverride, null, out var _, out var versionOverride ) )
                {
                    return false;
                }
                if( versionOverride != null )
                {
                    // A VersionOverride is exempt from the one version per identifier rule: under central
                    // package management it exists precisely to differ from the declared <PackageVersion>,
                    // and it is the only way to say so (a <PackageReference Version="..."/> alongside a
                    // <PackageVersion> is the NuGet error NU1008). It is not recorded as the first met one
                    // either: a later regular reference must be compared to the declaration, not to this.
                    _consumed.Add( new PackageInstance( packageId, versionOverride ) );
                    continue;
                }
                if( !CommonSolution.ReadVersionAttribute( monitor, path, e, XNames.Version, null, out var _, out var version ) )
                {
                    return false;
                }
                // A <PackageReference> with neither attribute contributes nothing: its version is
                // centrally managed and the <PackageVersion> declaration is what carries it.
                if( version != null && !AddConsumed( monitor, path, packageId, version ) )
                {
                    return false;
                }
            }
            return true;
        }
    }

    // Collects a package instance and enforces the one version per package identifier rule.
    //
    // A repository may reference a package identifier in ONE version only. This is deliberately decided
    // on the shallow read, which ignores every Condition: two conditional <PackageReference> across
    // target frameworks are two versions and are refused like any other pair. A single version per
    // identifier is what the whole dependency model is made of - the mapping that aligns and rewrites
    // references is a package identifier to version map and structurally cannot express two, and a
    // produced package carries one version for each of its dependencies.
    //
    // Discrepancies BETWEEN repositories are a different matter entirely: they are expected, transient
    // and healed by the build (the greatest referenced version wins). Only this repository local case is
    // an error, and it is one precisely because nothing can heal it: aligning it would silently rewrite
    // one of the two references and change what the developer wrote.
    bool AddConsumed( IActivityMonitor monitor, NormalizedPath path, string packageId, SVersion version )
    {
        if( _firstMet.TryGetValue( packageId, out var already ) )
        {
            if( already.Version != version )
            {
                monitor.Error( $"""
                                Package '{packageId}' is referenced in two versions by this repository:
                                '{already.Version}' in '{already.Path}'
                                '{version}' in '{path}'
                                A repository must reference one version per package identifier,
                                conditional references across target frameworks included: the dependency
                                alignment maps a package identifier to a single version and cannot
                                express two.
                                """ );
                return false;
            }
        }
        else
        {
            _firstMet.Add( packageId, (version, path) );
        }
        _consumed.Add( new PackageInstance( packageId, version ) );
        return true;
    }
}

