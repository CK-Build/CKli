using CK.Core;
using CSemVer;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Build
{
    /// <summary>
    /// Models a source of packages (packages are installable artifacts) inside a <see cref="PackageDB"/>.
    /// </summary>
    public class PackageFeed : IArtifactFeedIdentity
    {
        // We need a name that is a Artifact type and a name: Artifact does the job.
        readonly Artifact _name;
        readonly PackageDB.InstanceStore _instances;

        internal class Diff
        {
            List<(int idx, PackageInstance p)>? _new;
            List<int>? _old;

            internal Diff( PackageFeed f, (int idx, PackageInstance p) newOne )
            {
                Feed = f;
                _new = new List<(int idx, PackageInstance p)>() { newOne };
            }

            internal Diff( PackageFeed f, int oldIdx )
            {
                Feed = f;
                _old = new List<int>() { oldIdx };
            }

            public readonly PackageFeed Feed;

            public void AddNew( int idx, PackageInstance p )
            {
                Debug.Assert( _new != null && _new.Count > 0, "We have already allocated it (with its very first package)." );
                Debug.Assert( _old == null, "All AddNew must be called before AddOld." );
                Debug.Assert( idx >= 0 );
                _new.Add( (idx, p) );
            }

            public void AddOld( int oldIdx )
            {
                if( _old == null ) _old = new List<int>();
                _old.Add( oldIdx );
            }

            public PackageFeed Create()
            {
                Debug.Assert( _old != null || _new != null );
                return new PackageFeed( Feed, _new, _old );
            }
        }

        internal PackageFeed( in Artifact name, PackageDB.InstanceStore instances )
        {
            _name = name;
            _instances = instances;
            Debug.Assert( _instances.All( p => p.Key.Artifact.Type == _name.Type ) );
        }

        internal PackageFeed( PackageFeed other, List<PackageInstance> newPackages )
        {
            _name = other._name;
            _instances = other._instances.Add( newPackages );
            Debug.Assert( _instances.All( p => p.Key.Artifact.Type == _name.Type ) );
        }

        internal PackageFeed( PackageFeed other, List<(int idx, PackageInstance p)>? newPackages, List<int>? oldPackages )
        {
            _name = other._name;
            _instances = other._instances.Add( newPackages, oldPackages );
            Debug.Assert( _instances.All( p => p.Key.Artifact.Type == _name.Type ) );
        }

        internal PackageFeed( PackageDB.InstanceStore allInstances, DeserializerContext ctx )
        {
            var type = ArtifactType.Single( ctx.Reader.ReadSharedString() );
            var name = ctx.Reader.ReadString();
            _name = new Artifact( type, name );
            _instances = new PackageDB.InstanceStore( ctx, allInstances );
            Debug.Assert( _instances.All( p => p.Key.Artifact.Type == _name.Type ) );
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
        /// Gets the instance or null if not found.
        /// </summary>
        /// <param name="key">The package identifier.</param>
        /// <returns>The instance or null if not found.</returns>
        public PackageInstance? Find( in ArtifactInstance key ) => _instances.Find( key );

        internal int IndexOf( in ArtifactInstance key ) => _instances.IndexOf( key );

        /// <summary>
        /// Gets the available instances in this feed.
        /// </summary>
        /// <param name="this">This feed.</param>
        /// <param name="name">The package's name.</param>
        /// <returns>The available instances. <see cref="ArtifactAvailableInstances.IsValid"/> is false if the artifact is not found.</returns>
        public ArtifactAvailableInstances GetAvailableInstances( string name )
        {
            SVersion? ci = null, exp = null, pre = null, lat = null, sta = null;
            foreach( var p in GetInstances( name ) )
            {
                PackageQualityVector.Apply( p.Key.Version, ref ci, ref exp, ref pre, ref lat, ref sta );
            }
            var versions = new PackageQualityVector( ci, exp, pre, lat, sta );
            return new ArtifactAvailableInstances( this, name, versions );
        }

    }
}
