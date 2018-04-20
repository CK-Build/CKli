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
        public ReleaseLevel Level { get; }

        public ReleaseConstraint Constraint { get; }

        public CSVersion Version { get; }

        public bool IsValid => Version != null;

        ReleaseInfo( ReleaseLevel l, ReleaseConstraint c, CSVersion v )
        {
            Level = l;
            Constraint = c;
            Version = v;
        }

        public ReleaseInfo CombineRequirement( ReleaseInfo other )
        {
            if( Version != null ) throw new InvalidOperationException( "Version has been set. No more requirement combination can be done." );
            var l = Level > other.Level ? Level : other.Level;
            var c = Constraint | other.Constraint;
            return new ReleaseInfo( l, c, null );
        }

        public ReleaseInfo WithVersion( CSVersion v )
        {
            var c = v.IsPreRelease ? Constraint | ReleaseConstraint.MustBePreRelease : Constraint;
            return new ReleaseInfo( Level, c, v );
        }

        public ReleaseInfo WithLevel( ReleaseLevel level )
        {
            if( level <= Level ) return this;
            var c = Constraint;
            if( level == ReleaseLevel.BreakingChange ) c |= ReleaseConstraint.HasBreakingChanges;
            else if( level == ReleaseLevel.Feature ) c |= ReleaseConstraint.HasFeatures;
            return new ReleaseInfo( level, c, Version );
        }

        public override string ToString() => $"Level = {Level}, Constraint = {Constraint}";

    }
}
