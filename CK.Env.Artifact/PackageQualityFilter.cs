using CSemVer;
using System;

namespace CK.Env
{
    /// <summary>
    /// Defines a min/max filter of <see cref="PackageQuality"/>.
    /// </summary>
    public readonly struct PackageQualityFilter
    {
        /// <summary>
        /// Gets the minimal package quality. <see cref="PackageQuality.None"/> is the same as <see cref="PackageQuality.CI"/>.
        /// </summary>
        public PackageQuality Min { get; }


        /// <summary>
        /// Gets the maximal package quality. <see cref="PackageQuality.None"/> is the same as <see cref="PackageQuality.Release"/>.
        /// </summary>
        public PackageQuality Max { get; }

        /// <summary>
        /// Gets whether <see cref="Min"/> is relevant (not <see cref="PackageQuality.None"/> nor <see cref="PackageQuality.CI"/>).
        /// </summary>
        public bool HasMin => Min != PackageQuality.None && Min != PackageQuality.CI;

        /// <summary>
        /// Gets whether <see cref="Max"/> is relevant (not <see cref="PackageQuality.None"/> nor <see cref="PackageQuality.Release"/>).
        /// </summary>
        public bool HasMax => Max != PackageQuality.None && Max != PackageQuality.Release;

        /// <summary>
        /// Gets whether this filter allows the specified quality.
        /// </summary>
        /// <param name="q">The quality to challenge. <see cref="PackageQuality.None"/> is never accepted.</param>
        /// <returns>Whether <paramref name="q"/> is accepted or not.</returns>
        public bool Accepts( PackageQuality q ) => q != PackageQuality.None
                                                    && (!HasMin || q >= Min)
                                                    && (!HasMax || q <= Max);

        /// <summary>
        /// Initializes a new filter.
        /// </summary>
        /// <param name="min">The minimal quality.</param>
        /// <param name="max">The maximal quality.</param>
        public PackageQualityFilter( PackageQuality min, PackageQuality max )
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Initializes a new filter by parsing a nullable string.
        /// Throws an <see cref="ArgumentException"/> on invalid syntax: use <see cref="TryParse(string, out PackageQualityFilter)"/>
        /// to handle invalid syntax.
        /// </summary>
        /// <param name="s">The string. Can be null.</param>
        public PackageQualityFilter( string s )
        {
            if( s != null )
            {
                if( !TryParse( s, out PackageQualityFilter p ) ) throw new ArgumentException( "Invalid PackageQualityFilter syntax." );
                Min = p.Min;
                Max = p.Max;
            }
            else Min = Max = PackageQuality.None;
        }

        /// <summary>
        /// Overridden to return "<see cref="Min"/>,<see cref="Max"/>".
        /// </summary>
        /// <returns>The "Min,Max" string.</returns>
        public override string ToString() => Min.ToString() + ',' + Max.ToString();

        /// <summary>
        /// Attempts to parse a string as a <see cref="PackageQualityFilter"/>.
        /// Examples: "CI,Release", ",PreRelease", "Preview,Preview", "Preview,".
        /// </summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="q">The result.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool TryParse( string s, out PackageQualityFilter q )
        {
            var minMax = s.Replace( " ", "" )
                          .Split( ',' );
            if( minMax.Length == 2
                && TryParse( minMax[0], out PackageQuality min )
                && TryParse( minMax[1], out PackageQuality max ) )
            {
                q = new PackageQualityFilter( min, max );
                return true;
            }
            q = new PackageQualityFilter();
            return false;
        }

        static bool TryParse( string s, out PackageQuality q )
        {
            q = PackageQuality.None;
            if( s.Length == 0 ) return true;
            return Enum.TryParse<PackageQuality>( s, out q );
        }
    }
}
