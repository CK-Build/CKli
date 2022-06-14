using CK.Core;

using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Build
{
    /// <summary>
    /// Pure readonly interface that describes the basic information of a package handled by the <see cref="PackageDB"/>
    /// in an independent manner. This is used to import package information in the database.
    /// Once added, <see cref="PackageInstance"/> immutable objects are exposed.
    /// </summary>
    public interface IPackageInstanceInfo
    {
        /// <summary>
        /// Gets the key of this package (its type, name and version).
        /// </summary>
        ArtifactInstance Key { get; }

        PackageState State { get; }

        /// <summary>
        /// Gets the savors.
        /// It is null by default, can never be <see cref="CKTrait.IsEmpty"/>.
        /// See <see cref="PackageInstance.Savors"/> for explanations.
        /// </summary>
        CKTrait? Savors { get; }

        /// <summary>
        /// Gets the set of dependencies.
        /// This will be transformed into <see cref="PackageInstance.Dependencies"/> that is a set of <see cref="PackageInstance.Reference"/>.
        /// </summary>
        IEnumerable<(ArtifactInstance Target, SVersionLock Lock, PackageQuality MinQuality, ArtifactDependencyKind Kind, CKTrait? Savors)> Dependencies { get; }
    }

    /// <summary>
    /// Extends the <see cref="IPackageInstanceInfo"/>.
    /// </summary>
    public static class PackageInfoExtension
    {
        /// <summary>
        /// Checks whether the other information is the same as this one.
        /// Either logs an error and returns false if <paramref name="m"/> is available or throws an <see cref="ArgumentException"/>.
        /// Note that if the <see cref="IPackageInstanceInfo.Key"/> differ, an <see cref="ArgumentException"/> is always thrown since this is
        /// clearly a developer's bug.
        /// </summary>
        /// <param name="this">This package info.</param>
        /// <param name="m">Monitor to use instead of throwing an exception.</param>
        /// <returns>true if they are the same, false when they differ and a monitor is provided.</returns>
        public static bool CheckSame( this IPackageInstanceInfo @this, IPackageInstanceInfo other, IActivityMonitor? m = null )
        {
            static bool Error( string message, IActivityMonitor? m )
            {
                if( m == null ) throw new ArgumentException( message, nameof( FullPackageInstanceInfo ) );
                m.Error( message );
                return false;
            }
            if( other == null ) throw new ArgumentNullException( nameof( other ) );
            if( @this.Key != other.Key ) throw new ArgumentException( $"Cannot compare package info for '{@this.Key}' with the ones of '{other.Key}'.", nameof( other ) );
            if( @this.Savors != other.Savors ) return Error( $"Expected Savors to be '{@this.Savors}' , got '{other.Savors}'.", m );

            var depT = GetDepString( @this.Dependencies );
            var depO = GetDepString( other.Dependencies );

            if( depT != depO ) return Error( $"Expected dependencies to be {Environment.NewLine}'{depT}'{Environment.NewLine}But got:{Environment.NewLine}{depO}.", m );

            static string GetDepString( IEnumerable<(ArtifactInstance Target, SVersionLock Lock, PackageQuality MinQuality, ArtifactDependencyKind Kind, CKTrait? Savors)> deps )
            {
                return deps.OrderBy( d => d.Target ).ThenBy( d => d.Savors ).Select( d => $"{d.Target}, {d.Lock}, {d.MinQuality}, {d.Kind}" ).Concatenate( "', '" );
            }

            return true;
        }
    }
}
