using CK.Core;
using System;

namespace CK.Build
{
    /// <summary>
    /// Immutable description of artifacts type that can be registered
    /// from independent modules.
    /// Implements value equality through reference equality: uses <see cref="Name"/> with <see cref="StringComparer.Ordinal"/>.
    /// </summary>
    public sealed class ArtifactType : IComparable<ArtifactType>
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
        /// Never null.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the context where potential savors must be defined.
        /// If this type of artifact doesn't require (or support) savors, this is null.
        /// </summary>
        public CKTraitContext? ContextSavors { get; }

        static ArtifactType[] _types = Array.Empty<ArtifactType>();
        static readonly object _lock = new object();

        ArtifactType( string name, bool installable, char savorSeparator )
        {
            Name = name;
            IsInstallable = installable;
            ContextSavors = savorSeparator == 0 ? null : CKTraitContext.Create( "ArtifactType:" + name, savorSeparator );
        }

        /// <summary>
        /// Gets an already <see cref="Register"/>ed type or throws
        /// an <see cref="ArgumentException"/> if not found.
        /// </summary>
        /// <param name="name">Type name.</param>
        /// <returns>The single registered type.</returns>
        public static ArtifactType Single( string name )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            var types = _types;
            foreach( var t in types ) if( t.Name == name ) return t;
            Throw.ArgumentException( $"Unregistered Artifact type: '{name}'." );
            return null;
        }

        /// <summary>
        /// Gets an already <see cref="Register"/>ed type or null.
        /// </summary>
        /// <param name="name">Type name.</param>
        /// <returns>The single registered type or null.</returns>
        public static ArtifactType? SingleOrDefault( string name )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            var types = _types;
            foreach( var t in types ) if( t.Name == name ) return t;
            return null;
        }

        /// <summary>
        /// Registers a type (that may be already registered).
        /// This throws an <see cref="InvalidOperationException"/> if it is already registered
        /// with a different <see cref="IsInstallable"/> attribute.
        /// </summary>
        /// <param name="name">The type name.</param>
        /// <param name="isInstallable">Whether the type is installable.</param>
        /// <param name="savorSeparator">
        /// Optional support of <see cref="ContextSavors"/> by defining a separator.
        /// By default, no savors are supported and <see cref="ContextSavors"/> is null.
        /// </param>
        /// <returns>The registered type.</returns>
        public static ArtifactType Register( string name, bool isInstallable, char savorSeparator = '\0' )
        {
            ArtifactType? FindSame()
            {
                var t = SingleOrDefault( name );
                if( t != null )
                {
                    if( t.IsInstallable != isInstallable
                        || (t.ContextSavors?.Separator ?? '\0') != savorSeparator )
                    {
                        Throw.InvalidOperationException( $"Type {name} is already defined with IsInstallable/Savors=({t.IsInstallable},{(t.ContextSavors != null ? t.ContextSavors.Separator.ToString() : "(none)")}). It cannot be redefined with {isInstallable},{(savorSeparator != '\0' ? savorSeparator.ToString() : "(none)")}" );
                    }
                }
                return t;
            }

            var exists = FindSame();
            if( exists == null )
            {
                lock( _lock )
                {
                    exists = FindSame();
                    if( exists == null )
                    {
                        exists = new ArtifactType( name, isInstallable, savorSeparator );
                        Array.Resize( ref _types, _types.Length + 1 );
                        _types[_types.Length - 1] = exists;
                    }
                }
            }
            return exists;
        }

        /// <summary>
        /// Compares this type to another: <see cref="Name"/> is the order key.
        /// </summary>
        /// <param name="other">The other type to compare to. Can be null.</param>
        /// <returns>The negative/zero/positive standard value.</returns>
        public int CompareTo( ArtifactType? other ) => other != null ? String.Compare( Name, other.Name, StringComparison.Ordinal ) : 1;

        /// <summary>
        /// Returns the <see cref="Name"/>.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => Name;

    }
}
