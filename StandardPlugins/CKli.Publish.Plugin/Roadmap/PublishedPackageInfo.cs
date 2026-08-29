using CK.Core;
using CKli.Build.Plugin;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Publish.Plugin;

sealed class PublishedPackageInfo : PackageInstance
{
    object _reason;
    Dictionary<SVersion, object>? _conflicts;

    /// <summary>
    /// Root reason type.
    /// </summary>
    public abstract record Reason();

    /// <summary>
    /// Reason for packages produced by a <see cref="Roadmap"/>: this is the version the profile offers.
    /// </summary>
    /// <param name="Solution">The solution that produces this package.</param>
    public record PublishedByRoadmap( Roadmap.BuildSolution Solution ) : Reason;

    /// <summary>
    /// Reason for a version required by a solution of the <see cref="Roadmap"/>.
    /// </summary>
    /// <param name="Consumer">The solution that requires this version.</param>
    public record RequiredBySolution( Roadmap.BuildSolution Consumer ) : Reason;

    internal PublishedPackageInfo( string packageId, SVersion version, Reason firstReason )
        : base( packageId, version )
    {
        _reason = firstReason;
    }

    public IEnumerable<Reason> Reasons => _reason is Reason r ? [r] : (IEnumerable<Reason>)_reason;

    /// <summary>
    /// Gets whether <see cref="Conflicts"/> is not empty.
    /// </summary>
    public bool HasConflict => _conflicts != null;

    /// <summary>
    /// Gets the conflicts if any.
    /// </summary>
    public IEnumerable<(SVersion Version, IEnumerable<Reason> Reasons)> Conflicts
    {
        get
        {
            return _conflicts == null
                ? Enumerable.Empty<(SVersion Version, IEnumerable<Reason> Reasons)>()
                : _conflicts.Select( kv => (kv.Key, kv.Value is Reason r ? [r] : (IEnumerable<Reason>)kv.Value ) );
        }
    }

    internal bool Add( SVersion version, Reason reason )
    {
        if( Version == version )
        {
            if( _reason is List<Reason> list ) list.Add( reason );
            else _reason = new List<Reason>() { (Reason)_reason, reason };
            return true;
        }
        _conflicts ??= new Dictionary<SVersion, object>();
        if( _conflicts.TryGetValue( version, out var existingReason ) )
        {
            if( existingReason is List<Reason> list )
            {
                list.Add( reason );
            }
            else
            {
                _conflicts[version] = new List<Reason>() { (Reason)existingReason, reason };
            }
        }
        else
        {
            _conflicts.Add( version, reason );
        }
        return false;
    }
}

