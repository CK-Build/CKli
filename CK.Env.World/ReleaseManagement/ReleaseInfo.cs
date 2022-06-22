using CK.Core;
using CSemVer;
using System;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Immutable value type that encapsulates logic to build a <see cref="ReleaseLevel"/>
    /// and <see cref="ReleaseConstraint"/> with the final <see cref="Version"/> to use.
    /// A ReleaseInfo <see cref="IsValid"/> if and only if the <see cref="Version"/> has been set.
    /// </summary>
    public readonly struct ReleaseInfo
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
        public CSVersion? Version { get; }

        /// <summary>
        /// Gets whether this ReleaseInfo is valid: it has a <see cref="Version"/>.
        /// </summary>
        public bool IsValid => Version != null;

        ReleaseInfo( ReleaseLevel l, ReleaseConstraint c, CSVersion? v )
        {
            Level = l;
            Constraint = c;
            Version = v;
        }

        /// <summary>
        /// Creates a <see cref="ReleaseInfo"/> from a <see cref="XElement"/> (see <see cref="ToXml"/>).
        /// </summary>
        /// <param name="e">The Xml element.</param>
        public ReleaseInfo( XElement e )
        {
            var sV = (string?)e.Attribute( "Version" );
            Version = sV == null || sV.Length == 0 ? null : CSVersion.Parse( sV );
            Level = e.AttributeEnum( "Level", ReleaseLevel.None );
            Constraint = e.AttributeEnum( "Constraint", ReleaseConstraint.None );
        }

        /// <summary>
        /// Creates an xml element from this <see cref="ReleaseInfo"/>.
        /// When <see cref="IsValid"/> is false, there will be no Version attribute.
        /// </summary>
        /// <returns>A new XElement.</returns>
        public XElement ToXml()
        {
            return new XElement( XmlNames.xReleaseInfo,
                                    Version != null ? new XAttribute( XmlNames.xVersion, Version ) : null,
                                    new XAttribute( XmlNames.xLevel, Level ),
                                    new XAttribute( XmlNames.xConstraint, Constraint ) );
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
        /// Tests whether this <see cref="ReleaseInfo"/> is compatible with a given <see cref="ReleaseLevel"/>
        /// and <see cref="ReleaseConstraint"/>.
        /// </summary>
        /// <param name="l">The minimal level.</param>
        /// <param name="c">Constraints that must be satisfied.</param>
        /// <returns>True if this ReleaseInfo can be used with this level and constraint.</returns>
        public bool IsCompatibleWith( ReleaseLevel l, ReleaseConstraint c )
        {
            return Level >= l && (Constraint & c) == c;
        }

        /// <summary>
        /// Returns a valid new <see cref="ReleaseInfo"/> with the version set.
        /// </summary>
        /// <param name="v">The version to set. Can not be null.</param>
        /// <returns>A valid ReleaseInfo.</returns>
        public ReleaseInfo WithVersion( CSVersion v )
        {
            Throw.CheckNotNullArgument( v );
            var c = v.IsPrerelease && v.Major != 0
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
        /// Returns a requirement that cannot be <see cref="ReleaseLevel.BreakingChange"/> and <see cref="ReleaseConstraint.HasBreakingChanges"/>.
        /// If the <see cref="Version"/> is already resolved, this throws an <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <returns>A at most "feature level" release info.</returns>
        public ReleaseInfo LowerForSingleMajor()
        {
            Throw.CheckState( Version == null );
            var newLevel = Level;
            if( newLevel == ReleaseLevel.BreakingChange )
            {
                newLevel = ReleaseLevel.Feature;
            }
            var newConstraint = Constraint & ~ReleaseConstraint.HasBreakingChanges;
            if( newConstraint != Constraint ) newConstraint |= ReleaseConstraint.HasFeatures;
            return new ReleaseInfo( newLevel, newConstraint, null );
        }

        /// <summary>
        /// Returns a requirement that cannot be more than <see cref="ReleaseLevel.Fix"/> and has no
        /// <see cref="ReleaseConstraint.HasFeatures"/> or <see cref="ReleaseConstraint.HasBreakingChanges"/> constraints.
        /// If the <see cref="Version"/> is already resolved, this throws an <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <returns>A at most "fix level" release info.</returns>
        public ReleaseInfo LowerForOnlyPatch()
        {
            Throw.CheckState( Version == null );
            var newLevel = Level;
            if( newLevel >= ReleaseLevel.Feature )
            {
                newLevel = ReleaseLevel.Fix;
            }
            var newConstraint = Constraint & ~(ReleaseConstraint.HasFeatures|ReleaseConstraint.HasBreakingChanges);
            return new ReleaseInfo( newLevel, newConstraint, null );
        }

        /// <summary>
        /// Overridden to return a string with <see cref="Constraint"/>, <see cref="Level"/>
        /// and <see cref="Version"/> if this ReleaseInfo is valid.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => Version == null
                                                ? $"Level = {Level}, Constraint = {Constraint}"
                                                : $"{Version} - Level = {Level}, Constraint = {Constraint}";

    }
}
