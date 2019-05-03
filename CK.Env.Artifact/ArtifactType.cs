using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates registered types of artifacts.
    /// </summary>
    public readonly struct ArtifactType : IEquatable<ArtifactType>
    {
        /// <summary>
        /// Gets whether this type of artifact is installable.
        /// Installable artifacts can be "installed in" / "used by" other projects/solutions
        /// (they are typically called "packages": NuGet, NPM, etc. are installable artifacts).
        /// Non installable artifacts are produced by a "project" but are not aimed to be
        /// "consumed" by other projects/solutions (think logs, installers, Web site deployments, etc.).
        /// </summary>
        public bool IsInstallable { get; }

        /// <summary>
        /// Gets the type name. Typically "NuGet", "NPM", "CKSetup", etc.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets whether this type is the default, invalid, one.
        /// </summary>
        public bool IsDefault => Name == null;


        static ArtifactType[] _types = Array.Empty<ArtifactType>();
        static readonly object _lock = new object();

        ArtifactType( string name, bool installable )
        {
            Name = name;
            IsInstallable = installable;
        }

        /// <summary>
        /// Gets an already <see cref="Register"/>ed type or throws
        /// an <see cref="ArgumentException"/> if not found.
        /// </summary>
        /// <param name="name">Type name.</param>
        /// <returns>The single registered type.</returns>
        public static ArtifactType Single( string name ) 
        {
            var types = _types;
            foreach( var t in types ) if( t.Name == name ) return t;
            throw new ArgumentException( $"Unregistered Artifact type: '{name}'." );
        }

        /// <summary>
        /// Gets an already <see cref="Register"/>ed type or a <see cref="IsDefault"/> one.
        /// </summary>
        /// <param name="name">Type name.</param>
        /// <returns>The single registered type or a default one.</returns>
        public static ArtifactType SingleOrDefault( string name )
        {
            var types = _types;
            foreach( var t in types ) if( t.Name == name ) return t;
            return new ArtifactType();
        }

        /// <summary>
        /// Registers a type (that may be already registered).
        /// This throws an <see cref="InvalidOperationException"/> if it is already registered
        /// with a different <see cref="IsInstallable"/> attribute.
        /// </summary>
        /// <param name="name">The type name.</param>
        /// <param name="isInstallable">Whether the type is installable.</param>
        /// <returns>The reistered type.</returns>
        public static ArtifactType Register( string name, bool isInstallable )
        {
            ArtifactType FindSame()
            {
                var t = SingleOrDefault( name );
                if( !t.IsDefault )
                {
                    if( t.IsInstallable != isInstallable ) throw new InvalidOperationException();
                }
                return t;
            }
            var exists = FindSame();
            lock( _lock )
            {
                exists = FindSame();
                if( exists.IsDefault )
                {
                    exists = new ArtifactType( name, isInstallable );
                    Array.Resize( ref _types, _types.Length + 1 );
                    _types[_types.Length - 1] = exists;
                }
                return exists;
            }
        }

        /// <summary>
        /// Gets whether this type is equal to the other one.
        /// </summary>
        /// <param name="other">Other artifact type.</param>
        /// <returns>Whether they ahve the same name.</returns>
        public bool Equals( ArtifactType other ) => Name == other.Name;

        public static bool operator ==( in ArtifactType t1, in ArtifactType t2 ) => t1.Equals( t2 );

        public static bool operator !=( in ArtifactType t1, in ArtifactType t2 ) => !t1.Equals( t2 );

        public override bool Equals( object obj ) => obj is ArtifactType t && t.Equals( this );

        public override int GetHashCode() => Name.GetHashCode();

        public override string ToString() => Name;

    }
}
