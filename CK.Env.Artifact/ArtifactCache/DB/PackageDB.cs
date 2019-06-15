using System;
using CK.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CK.Text;

namespace CK.Env
{
    public partial class PackageDB
    {
        readonly InstanceStore _instances;
        readonly Dictionary<string, PackageFeed> _feeds;
        readonly int _version;

        public PackageDB()
        {
            _instances = new InstanceStore();
            _feeds = new Dictionary<string, PackageFeed>();
        }

        PackageDB( PackageDB origin, InstanceStore store, Dictionary<string, PackageFeed> newFeeds )
        {
            _version = origin._version + 1;
            _feeds = newFeeds ?? origin._feeds;
            _instances = store ?? origin._instances;
        }

        public PackageDB AddOrSkip( PackageInfo info )
        {
            var feedNames = info.CheckValidAndParseFeedNames();
            int idxNew = _instances.IndexOf( info.ArtifactInstance );
            if( idxNew >= 0 ) return this;

            var targets = info.Dependencies.Select( d => (d.Target, _instances.Find( d.Target )) ).ToArray();
            if( targets.Any( t => t.Item2 == null ) )
            {
                throw new InvalidOperationException( $"Dependency Target(s) not registered: {targets.Where( t => t.Item2 == null ).Select( t => t.Target.ToString() ).Concatenate()}" );
            }
            var deps = info.Dependencies.Zip( targets, ( d, t ) => new PackageInstance.Reference( t.Item2, d.Kind, d.Savors ) )
                           .ToArray();  

            var newOne = new PackageInstance( info.ArtifactInstance, info.Savors, deps );

            Dictionary<string, PackageFeed> newFeeds = null;
            if( feedNames.Length > 0 )
            {
                newFeeds = new Dictionary<string, PackageFeed>( _feeds );
                foreach( var name in feedNames )
                {
                    var keyName = name.TypedName;
                    if( _feeds.TryGetValue( keyName, out var feed ) )
                    {
                        newFeeds[keyName] = new PackageFeed( feed, newOne );
                    }
                    else
                    {
                        newFeeds.Add( keyName, new PackageFeed( name, new InstanceStore( newOne ) ) );
                    }
                }
            }
            return new PackageDB( this, new InstanceStore( _instances, newOne, ~idxNew ), newFeeds );
        }


        /// <summary>
        /// Gets all the feeds.
        /// </summary>
        public IEnumerable<PackageFeed> Feeds => _feeds.Values;

        /// <summary>
        /// Finds a feed by its <see cref="IArtifactFeedIdentity.TypedName"/>.
        /// </summary>
        /// <param name="typedName">The type name.</param>
        /// <returns>The feed or null.</returns>
        public PackageFeed FindFeedOrDefault( string typedName ) => _feeds.GetValueOrDefault( typedName );

        /// <summary>
        /// Gets the whole list of known packages.
        /// </summary>
        public IReadOnlyList<PackageInstance> Instances => _instances;

        /// <summary>
        /// Gets all the instances of a given type.
        /// </summary>
        /// <param name="type">The package's type.</param>
        /// <returns>The list of the known instances.</returns>
        public ReadOnlySpan<PackageInstance> GetInstances( ArtifactType type )
        {
            return _instances.GetInstances( type );
        }

        /// <summary>
        /// Gets all the instances of a package.
        /// </summary>
        /// <param name="package">The package.</param>
        /// <returns>The list of the known instances.</returns>
        public ReadOnlySpan<PackageInstance> GetInstances( Artifact package )
        {
            return _instances.GetInstances( package );
        }

    }
}
