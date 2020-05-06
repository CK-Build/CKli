using CK.Core;
using CSemVer;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Models a source of packages (packages are installable artifacts).
    /// </summary>
    public class PackageFeed : IArtifactFeedIdentity
    {
        // We need a name that is a Artifact type and a name: Artifact does the job.
        readonly Artifact _name;
        readonly PackageDB.InstanceStore _instances;

        internal PackageFeed( in Artifact name, PackageDB.InstanceStore instances )
        {
            _name = name;
            _instances = instances;
        }

        internal PackageFeed( PackageFeed other, List<PackageInstance> newPackages )
        {
            _name = other._name;
            _instances = other._instances.Add( newPackages );
        }

        internal PackageFeed( PackageDB.InstanceStore allInstances, DeserializerContext ctx )
        {
            var type = Core.ArtifactType.Single( ctx.Reader.ReadSharedString() );
            var name = ctx.Reader.ReadString();
            _name = new Artifact( type, name );
            _instances = new PackageDB.InstanceStore( ctx, allInstances );
        }

        internal void Write( PackageDB.InstanceStore allInstances, SerializerContext ctx )
        {
            ctx.Writer.WriteSharedString( _name.Type.Name );
            ctx.Writer.Write( _name.Name );
            _instances.WriteIndices( ctx, allInstances );
        }

        /// <summary>
        /// Name of this feed. Must be unique for the <see cref="ArtifactType"/>.
        /// </summary>
        public string Name => _name.Name;

        /// <summary>
        /// Identifies this feed. This is "<see cref="ArtifactType.Name"/>:<see cref="Name"/>" and must
        /// uniquely identify this feed.
        /// </summary>
        public string TypedName => _name.TypedName;

        /// <summary>
        /// Gets the artifact type that this feed supports.
        /// </summary>
        public ArtifactType ArtifactType => _name.Type;

        /// <summary>
        /// Gets the list of all the packages that this feed contains.
        /// </summary>
        public IReadOnlyList<PackageInstance> Instances => _instances;

        /// <summary>
        /// Gets all the instances of a given <see cref="ArtifactType"/>.
        /// </summary>
        /// <param name="type">The package's type.</param>
        /// <returns>The list of the known instances.</returns>
        public IReadOnlyList<PackageInstance> GetInstances( ArtifactType type )
        {
            return _instances.GetInstances( type );
        }

        /// <summary>
        /// Gets all the instances of a given package (of type <see cref="ArtifactType"/>).
        /// </summary>
        /// <param name="name">The package's name.</param>
        /// <returns>The list of the known instances.</returns>
        public IReadOnlyList<PackageInstance> GetInstances( string name )
        {
            var a = new Artifact( _name.Type, name );
            return _instances.GetInstances( a );
        }

        /// <summary>
        /// Gets the available instances in this feed.
        /// </summary>
        /// <param name="this">This feed.</param>
        /// <param name="artifactName">The artifact name.</param>
        /// <returns>The available instances. <see cref="ArtifactAvailableInstances.IsValid"/> is false if the artifact is not found.</returns>
        public ArtifactAvailableInstances GetAvailableInstances( string artifactName )
        {
            SVersion? ci = null, exp = null, pre = null, lat = null, sta = null;
            foreach( var p in GetInstances( artifactName ) )
            {
                PackageQualityVersions.Apply( p.Key.Version, ref ci, ref exp, ref pre, ref lat, ref sta );
            }
            var versions = new PackageQualityVersions( ci, exp, pre, lat, sta );
            return new ArtifactAvailableInstances( this, artifactName, versions );
        }

    }
}
