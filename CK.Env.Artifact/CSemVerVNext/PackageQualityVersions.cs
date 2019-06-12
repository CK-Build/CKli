using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CSemVer
{
    /// <summary>
    /// Handles 5 potentially different versions associated to <see cref="PackageQuality"/>.
    /// </summary>
    public readonly struct PackageQualityVersions : IEnumerable<SVersion>
    {
        /// <summary>
        /// Initializes a new <see cref="PackageQualityVersions"/> from a set of versions.
        /// </summary>
        /// <param name="versions">Set of available versions.</param>
        public PackageQualityVersions( IEnumerable<SVersion> versions )
        {
            CI = Exploratory = Preview = Latest = Stable = null;
            foreach( var v in versions )
            {
                if( v == null || !v.IsValid ) continue;
                switch( v.PackageQuality )
                {
                    case PackageQuality.Release: if( v > Stable ) Stable = v; goto case PackageQuality.ReleaseCandidate;
                    case PackageQuality.ReleaseCandidate: if( v > Latest ) Latest = v; goto case PackageQuality.Preview;
                    case PackageQuality.Preview: if( v > Preview ) Preview = v; goto case PackageQuality.Exploratory;
                    case PackageQuality.Exploratory: if( v > Exploratory ) Exploratory = v; goto default;
                    default: if( v > CI ) CI = v; break;
                }
            }
        }

        PackageQualityVersions( PackageQualityVersions q, SVersion v )
        {
            Debug.Assert( v?.IsValid ?? false );
            CI = q.CI;
            Exploratory = q.Exploratory;
            Preview = q.Preview;
            Latest = q.Latest;
            Stable = q.Stable;
            switch( v.PackageQuality )
            {
                case PackageQuality.Release: if( v > Stable ) Stable = v; goto case PackageQuality.ReleaseCandidate;
                case PackageQuality.ReleaseCandidate: if( v > Latest ) Latest = v; goto case PackageQuality.Preview;
                case PackageQuality.Preview: if( v > Preview ) Preview = v; goto case PackageQuality.Exploratory;
                case PackageQuality.Exploratory: if( v > Exploratory ) Exploratory = v; goto default;
                default: if( v > CI ) CI = v; break;
            }
        }

        /// <summary>
        /// Gets whether this <see cref="PackageQualityVersions"/> is valid: at least <see cref="CI"/>
        /// is available.
        /// </summary>
        public bool IsValid => CI != null;

        /// <summary>
        /// Gets the best version for a given quality or null if no such version exists.
        /// </summary>
        /// <param name="quality">The minimal required quality.</param>
        /// <returns>The best version or null if not found.</returns>
        public SVersion GetVersion( PackageQuality quality )
        {
            if( !IsValid ) throw new InvalidOperationException();
            switch( quality )
            {
                case PackageQuality.Release: return Stable;
                case PackageQuality.ReleaseCandidate: return Latest;
                case PackageQuality.Preview: return Preview;
                case PackageQuality.Exploratory: return Exploratory;
                default: return CI;
            }
        }

        /// <summary>
        /// Gets the best version for a given label or null if no such version exists.
        /// </summary>
        /// <param name="label">The required label.</param>
        /// <returns>The best version or null if not found.</returns>
        public SVersion GetVersion( PackageLabel label )
        {
            switch( label )
            {
                case PackageLabel.Stable: return Stable;
                case PackageLabel.Latest: return Latest;
                case PackageLabel.Preview: return Preview;
                case PackageLabel.Exploratory: return Exploratory;
                default: return CI;
            }
        }

        /// <summary>
        /// Gets the best stable version or null if no such version exists.
        /// </summary>
        public SVersion Stable { get; }

        /// <summary>
        /// Gets the best latest compatible version or null if no such version exists.
        /// </summary>
        public SVersion Latest { get; }

        /// <summary>
        /// Gets the best preview compatible version or null if no such version exists.
        /// </summary>
        public SVersion Preview { get; }

        /// <summary>
        /// Gets the best exploratory compatible version or null if no such version exists.
        /// </summary>
        public SVersion Exploratory { get; }

        /// <summary>
        /// Gets the best version or null if <see cref="IsValid"/> is false.
        /// </summary>
        public SVersion CI { get; }

        /// <summary>
        /// Retuns this <see cref="PackageQualityVersions"/> or a new one that combines a new version.
        /// </summary>
        /// <param name="v">Version to handle. May be null or invalid.</param>
        /// <returns>The QualityVersions.</returns>
        public PackageQualityVersions WithVersion( SVersion v )
        {
            if( v == null || !v.IsValid ) return this;
            return IsValid ? new PackageQualityVersions( this, v ) : new PackageQualityVersions( new[] { v } );
        }

        /// <summary>
        /// Retuns this <see cref="PackageQualityVersions"/> or a new one combined with another one.
        /// </summary>
        /// <param name="v">Other versions to be combined.</param>
        /// <returns>The resulting QualityVersions.</returns>
        public PackageQualityVersions With( PackageQualityVersions other )
        {
            if( !IsValid ) return other;
            if( !other.IsValid ) return this;
            return new PackageQualityVersions( other.Concat( this ) );
        }

        /// <summary>
        /// Overridden to return the non null versions separated by /.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString()
        {
            if( CI == null ) return String.Empty;
            var b = new StringBuilder();
            b.Append( CI.ToString() );
            if( Exploratory != null && Exploratory != CI )
            {
                b.Append( " / " ).Append( Exploratory.ToString() );
            }
            if( Preview != null && Preview != Exploratory )
            {
                b.Append( " / " ).Append( Preview.ToString() );
            }
            if( Latest != null && Latest != Preview )
            {
                b.Append( " / " ).Append( Latest.ToString() );
            }
            if( Stable != null && Stable != Latest )
            {
                b.Append( " / " ).Append( Stable.ToString() );
            }
            return b.ToString();
        }

        /// <summary>
        /// Returns the distinct CI, Exploratory, Preview, Latest, Stable (in this order) as long as they are not null.
        /// </summary>
        /// <returns>The set of distinct versions (empty if <see cref="IsValid"/> is false).</returns>
        public IEnumerator<SVersion> GetEnumerator()
        {
            if( CI != null )
            {
                yield return CI;
                if( Exploratory != null )
                {
                    if( Exploratory != CI )  yield return Exploratory;
                    if( Preview != null )
                    {
                        if( Preview != Exploratory ) yield return Preview;
                        if( Latest != null )
                        {
                            if( Latest != Preview ) yield return Latest;
                            if( Stable != null && Stable != Latest )
                            {
                                yield return Stable;
                            }
                        }
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
