using CK.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Build.PackageDB
{
    /// <summary>
    /// Pure readonly interface used to describe a new package in a <see cref="PackageDatabase"/>.
    /// </summary>
    public interface IFullPackageInfo : IPackageInstanceInfo
    {
        /// <summary>
        /// Gets or sets whether the <see cref="FeedNames"/> are all the feeds that contain this package.
        /// When false, feed names are only a subset of the feeds that contain this package.
        /// </summary>
        bool AllFeedNamesAreKnown { get; set; }

        /// <summary>
        /// Gets the name of the feeds that are known to contain this package.
        /// This can be empty: a package can exist in a <see cref="PackageDatabase"/> without being in any <see cref="PackageFeed"/>.
        /// </summary>
        IEnumerable<string> FeedNames { get; }
    }

    /// <summary>
    /// Provides the validation function to any <see cref="IFullPackageInfo"/> before adding them
    /// to a <see cref="PackageDatabase"/>.
    /// </summary>
    public static class FullPackageInfoExtension
    {
        /// <summary>
        /// Checks that this information is valid (in terms of naming, savors, dependencies and feed names) and
        /// returns the array of necessarily valid <see cref="Artifact"/> names of feeds (that can be empty if
        /// no feed name is provided).
        /// If something is invalid, either logs an error and returns null if <paramref name="m"/> is available
        /// or throws an <see cref="ArgumentException"/>.
        /// </summary>
        /// <param name="this">This package info.</param>
        /// <param name="m">Monitor to use instead of throwing an exception.</param>
        /// <returns>Not null valid feed names when valid, null when invalid and a monitor is provided.</returns>
        public static Artifact[]? CheckValidAndParseFeedNames( this IFullPackageInfo @this, IActivityMonitor? m = null )
        {
            static Artifact[]? Error( string message, IActivityMonitor? m )
            {
                if( m == null ) throw new ArgumentException( message, nameof( FullPackageInstanceInfo ) );
                m.Error( message );
                return null;
            }

            // Checking name.
            if( !@this.Key.IsValid )
            {
                return Error( $"Invalid ArtifactInstance.", m );
            }

            // Checking FeedNames.
            var feedNames = @this.FeedNames.Select( n => Artifact.TryParseOrCreate( n, @this.Key.Artifact.Type ) ).ToArray();
            if( feedNames.Any( f => !f.IsValid ) )
            {
                return Error( $"Invalid feed names found in '{@this.FeedNames.Concatenate( "', '" )}'.", m );
            }

            // Checking dependencies.
            if( @this.Dependencies.Any( d => d.Kind == ArtifactDependencyKind.None ) )
            {
                return Error( $"Dependencies cannot have ArtifactDependencyKind.None kind.", m );
            }
            if( @this.Dependencies.Any( d => !d.Target.IsValid ) )
            {
                return Error( $"Dependencies contain an invalid Target.", m );
            }
            // Checking Savors.
            if( @this.Savors == null )
            {
                if( @this.Dependencies.Any( d => d.Savors != null ) )
                {
                    return Error( $"PackageInfo has no Savors defined: dependencies cannot specify savors.", m );
                }
            }
            else
            {
                Debug.Assert( !@this.Savors.IsEmpty );
                var aliens = @this.Dependencies.Where( d => d.Savors != null && d.Savors.Context != @this.Savors.Context ).Select( d => d.Savors!.Context.Name );
                if( aliens.Any() )
                {
                    return Error( $"PackageInfo defines {@this.Savors} in '{@this.Savors.Context.Name}' context but dependencies are defined in '{aliens.Concatenate( "', '" )}' context(s).", m );
                }
                else
                {
                    var undefined = @this.Dependencies.Where( d => d.Savors != null )
                                             .SelectMany( d => d.Savors!.Except( @this.Savors ).AtomicTraits )
                                             .Distinct()
                                             .Select( t => t.ToString() );
                    if( undefined.Any() )
                    {
                        return Error( $"PackageInfo defines {@this.Savors} but dependencies have {undefined.Concatenate( @this.Savors.Context.Separator.ToString() )} undefined savors.", m );
                    }
                }
            }
            return feedNames;
        }
    }
}
