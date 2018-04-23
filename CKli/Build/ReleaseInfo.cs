using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    /// <summary>
    /// Encpasulates logic to build a <see cref="ReleaseLevel"/> and <see cref="ReleaseConstraint"/>.
    /// </summary>
    public struct ReleaseInfo
    {
        /// <summary>
        /// Gets the release level.
        /// </summary>
        public ReleaseLevel Level { get; }

        /// <summary>
        /// Gets the release constraint.
        /// </summary>
        public ReleaseConstraint Constraint { get; }

        /// <summary>
        /// Gets the final version to use.
        /// Can be null if this <see cref="IsValid"/> is (still) false. 
        /// </summary>
        public CSVersion Version { get; }

        /// <summary>
        /// Gets whether this ReleaseInfo is valid: it has a <see cref="Version"/>.
        /// </summary>
        public bool IsValid => Version != null;

        ReleaseInfo( ReleaseLevel l, ReleaseConstraint c, CSVersion v )
        {
            Level = l;
            Constraint = c;
            Version = v;
        }

        /// <summary>
        /// Combines this <see cref="ReleaseInfo"/> with another one and returns the result.
        /// This must no more be called once a <see cref="Version"/> is set otherwise an <see cref="InvalidOperationException"/>
        /// is thrown.
        /// </summary>  
        /// <param name="other">The other ReleasInfo to combine.</param>
        /// <returns>The result.</returns>
        public ReleaseInfo CombineRequirement( ReleaseInfo other )
        {
            if( Version != null ) throw new InvalidOperationException( "Version has been set. No more requirement combination can be done." );
            var l = Level > other.Level ? Level : other.Level;
            var c = Constraint | other.Constraint;
            return new ReleaseInfo( l, c, null );
        }

        /// <summary>
        /// Returns a valid new <see cref="ReleaseInfo"/> with the version set.
        /// </summary>
        /// <param name="v">The version to set. Can not be null.</param>
        /// <returns>A valid ReleaseInfo.</returns>
        public ReleaseInfo WithVersion( CSVersion v )
        {
            if( v == null ) throw new ArgumentNullException( nameof( v ) );
            var c = v.IsPreRelease && v.Major != 0
                        ? Constraint | ReleaseConstraint.MustBePreRelease
                        : Constraint;
            return new ReleaseInfo( Level, c, v );
        }

        /// <summary>
        /// Returns a new <see cref="ReleaseInfo"/> with a combined level that can never decrease.
        /// </summary>
        /// <param name="level">The level to set.</param>
        /// <returns>This info if <paramref name="level"/> is lower than or equal to this <see cref=Level"/> or a new ReleaseInfo.</returns>
        public ReleaseInfo WithLevel( ReleaseLevel level )
        {
            if( level <= Level ) return this;
            var c = Constraint;
            if( level == ReleaseLevel.BreakingChange ) c |= ReleaseConstraint.HasBreakingChanges;
            else if( level == ReleaseLevel.Feature ) c |= ReleaseConstraint.HasFeatures;
            return new ReleaseInfo( level, c, Version );
        }

        /// <summary>
        /// Overridden to return a sitring with <see cref="Constraint"/>, <see cref="Level"/>
        /// and <see cref="Version"/> if this ReleaseInfo is valid.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => Version == null
                                                ? $"Level = {Level}, Constraint = {Constraint}"
                                                : $"{Version} - Level = {Level}, Constraint = {Constraint}";

    }
}
