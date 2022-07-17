using CK.Core;

using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Build.PackageDB
{
    /// <summary>
    /// Pure readonly interface that describes the basic information of a package handled by the <see cref="PackageDatabase"/>
    /// in an independent manner. This is used to import package information in the database.
    /// Once added, <see cref="PackageInstance"/> immutable objects are exposed.
    /// </summary>
    public interface IPackageInstanceInfo
    {
        /// <summary>
        /// Gets the key of this package (its type, name and version).
        /// </summary>
        ArtifactInstance Key { get; }

        /// <summary>
        /// Gets the state of this instance.
        /// </summary>
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
        /// <para>
        /// This doesn't check the <see cref="IPackageInstanceInfo.State"/> and this is intended: this "same" methods
        /// tests the equality of the immutable aspect of a package, NOT the attributes that may change like feeds or state.
        /// </para>
        /// </summary>
        /// <param name="this">This package info.</param>
        /// <param name="m">Monitor to use instead of throwing an exception.</param>
        /// <returns>true if they are the same, false when they differ and a monitor is provided.</returns>
        public static bool CheckSameContent( this IPackageInstanceInfo @this, IPackageInstanceInfo other, IActivityMonitor? m = null )
        {
            static bool Error( string message, IActivityMonitor? m )
            {
                if( m == null ) Throw.ArgumentException( message, nameof( FullPackageInstanceInfo ) );
                m.Error( message );
                return false;
            }
            Throw.CheckNotNullArgument( other );
            Throw.CheckArgument( @this.Key == other.Key );
            Throw.CheckArgument( @this.Savors == other.Savors );

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
