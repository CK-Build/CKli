using CK.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Maps a package identifier to the <see cref="SVersionBound"/> its versions must stay in: a version that
/// doesn't <see cref="SVersionBound.Satisfy(in SVersion)"/> its bound is mapped to the bound's
/// <see cref="SVersionBound.Base"/>, a version that satisfies it is left alone.
/// <para>
/// A <see cref="SVersionLock.Lock"/>ed bound accepts its base version only: such a mapper behaves exactly
/// like a <see cref="BrutalPackageMapper"/> on the base version.
/// </para>
/// </summary>
public static class BoundPackageMapper
{
    /// <summary>
    /// Creates a mapper from a package identifier to version bound dictionary.
    /// <para>
    /// The dictionary MUST use the <see cref="StringComparer.OrdinalIgnoreCase"/> comparer otherwise an <see cref="ArgumentException"/>
    /// is thrown.
    /// </para>
    /// </summary>
    /// <param name="bounds">The package to version bound dictionary. When null, <see cref="PackageMapper.Empty"/> is returned.</param>
    /// <returns>A bound mapping.</returns>
    public static IPackageMapping Create( IReadOnlyDictionary<string, SVersionBound>? bounds )
    {
        Throw.CheckArgument( bounds is not Dictionary<string, SVersionBound> d || d.Comparer == StringComparer.OrdinalIgnoreCase );
        Throw.CheckArgument( bounds is not ConcurrentDictionary<string, SVersionBound> c || c.Comparer == StringComparer.OrdinalIgnoreCase );
        return bounds != null ? new FromDictionary( bounds ) : PackageMapper.Empty;
    }

    sealed class FromDictionary : IPackageMapping
    {
        readonly IReadOnlyDictionary<string, SVersionBound> _bounds;

        public FromDictionary( IReadOnlyDictionary<string, SVersionBound> bounds ) => _bounds = bounds;

        public bool IsEmpty => _bounds.Count == 0;

        public bool HasMapping( string packageId ) => _bounds.ContainsKey( packageId );

        public SVersion? GetMappedVersion( string packageId, SVersion from )
        {
            return _bounds.TryGetValue( packageId, out var bound ) && !bound.Satisfy( from )
                    ? bound.Base
                    : null;
        }

        public override string ToString()
        {
            var b = new StringBuilder();
            foreach( var (p, bound) in _bounds )
            {
                b.Append( p ).Append( " ∈ " ).Append( bound.ToString() ).AppendLine();
            }
            return b.ToString();
        }
    }

}
