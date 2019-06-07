using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CSemVer
{
    /// <summary>
    /// Handles 5 potentially different versions associated to <see cref="PackageQuality"/>.
    /// </summary>
    public readonly struct PackageQualityVersions
    {
        /// <summary>
        /// Initializes a new <see cref="PackageQualityVersions"/> from a set of versions.
        /// </summary>
        /// <param name="versions">Set of available versions.</param>
        public PackageQualityVersions( IEnumerable<SVersion> versions )
        {
            BestCI = BestExploratory = BestPreview = BestLatest = BestStable = null;
            foreach( var v in versions )
            {
                if( v == null || !v.IsValid ) continue;
                switch( v.PackageQuality )
                {
                    case PackageQuality.Release: if( v > BestStable ) BestStable = v; goto case PackageQuality.ReleaseCandidate;
                    case PackageQuality.ReleaseCandidate: if( v > BestLatest ) BestLatest = v; goto case PackageQuality.Preview;
                    case PackageQuality.Preview: if( v > BestPreview ) BestPreview = v; goto case PackageQuality.Exploratory;
                    case PackageQuality.Exploratory: if( v > BestExploratory ) BestExploratory = v; goto default;
                    default: if( v > BestCI ) BestCI = v; break;
                }
            }
        }

        PackageQualityVersions( PackageQualityVersions q, SVersion v )
        {
            Debug.Assert( v?.IsValid ?? false );
            BestCI = q.BestCI;
            BestExploratory = q.BestExploratory;
            BestPreview = q.BestPreview;
            BestLatest = q.BestLatest;
            BestStable = q.BestStable;
            switch( v.PackageQuality )
            {
                case PackageQuality.Release: if( v > BestStable ) BestStable = v; goto case PackageQuality.ReleaseCandidate;
                case PackageQuality.ReleaseCandidate: if( v > BestLatest ) BestLatest = v; goto case PackageQuality.Preview;
                case PackageQuality.Preview: if( v > BestPreview ) BestPreview = v; goto case PackageQuality.Exploratory;
                case PackageQuality.Exploratory: if( v > BestExploratory ) BestExploratory = v; goto default;
                default: if( v > BestCI ) BestCI = v; break;
            }
        }

        /// <summary>
        /// Gets whether this <see cref="PackageQualityVersions"/> is valid: at least <see cref="BestCI"/>
        /// is available.
        /// </summary>
        public bool IsValid => BestCI != null;

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
                case PackageQuality.Release: return BestStable;
                case PackageQuality.ReleaseCandidate: return BestLatest;
                case PackageQuality.Preview: return BestPreview;
                case PackageQuality.Exploratory: return BestExploratory;
                default: return BestCI;
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
                case PackageLabel.Stable: return BestStable;
                case PackageLabel.Latest: return BestLatest;
                case PackageLabel.Preview: return BestPreview;
                case PackageLabel.Exploratory: return BestExploratory;
                default: return BestCI;
            }
        }

        /// <summary>
        /// Gets the best stable version or null if no such version exists.
        /// </summary>
        public SVersion BestStable { get; }

        /// <summary>
        /// Gets the best latest compatible version or null if no such version exists.
        /// </summary>
        public SVersion BestLatest { get; }

        /// <summary>
        /// Gets the best preview compatible version or null if no such version exists.
        /// </summary>
        public SVersion BestPreview { get; }

        /// <summary>
        /// Gets the best exploratory compatible version or null if no such version exists.
        /// </summary>
        public SVersion BestExploratory { get; }

        /// <summary>
        /// Gets the best version or null if <see cref="IsValid"/> is false.
        /// </summary>
        public SVersion BestCI { get; }

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
        /// Overridden to return the non null versions separated by /.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString()
        {
            if( BestCI == null ) return String.Empty;
            var b = new StringBuilder();
            b.Append( BestCI.ToString() );
            if( BestExploratory != null && BestExploratory != BestCI )
            {
                b.Append( " / " ).Append( BestExploratory.ToString() );
            }
            if( BestPreview != null && BestPreview != BestExploratory )
            {
                b.Append( " / " ).Append( BestPreview.ToString() );
            }
            if( BestLatest != null && BestLatest != BestPreview )
            {
                b.Append( " / " ).Append( BestLatest.ToString() );
            }
            if( BestStable != null && BestStable != BestLatest )
            {
                b.Append( " / " ).Append( BestStable.ToString() );
            }
            return b.ToString();
        }
    }

}
