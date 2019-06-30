using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CK.Core;
using CK.Text;

namespace CK.Env
{
    /// <summary>
    /// Basic POCO used to describe a new package in a <see cref="PackageDB"/>.
    /// </summary>
    public class PackageInfo
    {
        CKTrait _savors;

        /// <summary>
        /// Gets or sets the key of this package.
        /// </summary>
        public ArtifactInstance Key { get; set; }

        /// <summary>
        /// Gets the name of the feeds that are known to contain this package.
        /// </summary>
        public List<string> FeedNames { get; } = new List<string>();

        /// <summary>
        /// Gets or sets the savors if any.
        /// <see cref="CKTrait.IsEmpty"/> is forbidden and raises an ArgumentExceptiuon if set.
        /// </summary>
        public CKTrait Savors
        {
            get => _savors;
            set
            {
                if( value != null && value.IsEmpty ) throw new ArgumentException( "PackageInfo Savors cannot be empty.", "value" );
                _savors = value;
            }
        }

        /// <summary>
        /// Gets the mutable list of dependencies.
        /// </summary>
        public List<(ArtifactInstance Target, ArtifactDependencyKind Kind, CKTrait Savors)> Dependencies { get; } = new List<(ArtifactInstance, ArtifactDependencyKind, CKTrait)>();

        /// <summary>
        /// Checks that this information is valid (in terms of naming, savors, dependencies and feed names) and
        /// returns the array of necessarily valid <see cref="Artifact"/> names of feeds (that can be empty if
        /// no feed name is provided).
        /// If something is invalid, either logs an error and returns null if <paramref name="m"/> is available
        /// or throws an <see cref="ArgumentException"/>.
        /// </summary>
        /// <param name="m">Monitor to use instead of throwing an exception.</param>
        /// <returns>Not null valid feed names when valid, null when invalid and a monitor is provided.</returns>
        public Artifact[] CheckValidAndParseFeedNames( IActivityMonitor m = null )
        {
            Artifact[] Error( string message )
            {
                if( m == null ) throw new ArgumentException( message, nameof( PackageInfo ) );
                m.Error( message );
                return null;
            }
            // Cheking name.
            if( !Key.IsValid )
            {
                return Error( $"Invalid ArtifactInstance." );
            }

            // Checking FeedNames.
            var feedNames = FeedNames.Select( n => Artifact.TryParseOrCreate( n, Key.Artifact.Type ) ).ToArray();
            if( feedNames.Any( f => !f.IsValid ) )
            {
                return Error( $"Invalid feed names found in '{FeedNames.Concatenate("', '")}'." );
            }

            // Checking dependencies.
            if( Dependencies.Any( d => d.Kind == ArtifactDependencyKind.None ) )
            {
                return Error( $"Dependencies cannot have ArtifactDependencyKind.None kind." );
            }
            if( Dependencies.Any( d => !d.Target.IsValid ) )
            {
                return Error( $"Dependencies contain an invalid Target." );
            }
            // Checking Savors.
            if( Savors == null )
            {
                if( Dependencies.Any( d => d.Savors != null ) )
                {
                    return Error( $"PackageInfo has no Savors defined: dependencies cannot specify savors." );
                }
            }
            else
            {
                Debug.Assert( !Savors.IsEmpty );
                var aliens = Dependencies.Where( d => d.Savors != null && d.Savors.Context != Savors.Context ).Select( d => d.Savors.Context.Name );
                if( aliens.Any() )
                {
                    return Error( $"PackageInfo defines {Savors} in '{Savors.Context.Name}' context but dependencies are defined in '{aliens.Concatenate( "', '" )}' context(s)." );
                }
                else
                {
                    var undefined = Dependencies.Where( d => d.Savors != null )
                                             .SelectMany( d => d.Savors.Except( Savors ).AtomicTraits )
                                             .Distinct()
                                             .Select( t => t.ToString() );
                    if( undefined.Any() )
                    {
                        return Error( $"PackageInfo defines {Savors} but dependencies have {undefined.Concatenate( Savors.Context.Separator.ToString() )} undefined savors." );
                    }
                }
            }
            return feedNames;
        }
    }
}
