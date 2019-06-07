using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using CK.Core;

namespace CK.Env
{
    /// <summary>
    /// Combination of <see cref="IArtifactTypeHandler"/>.
    /// Actual management of repositories and infos are totally delegated to the type handlers, this
    /// object doesn't track repositories or repository infos.
    /// </summary>
    public class ArtifactCenter
    {
        readonly List<IArtifactTypeHandler> _typeHandlers;

        public ArtifactCenter()
        {
            _typeHandlers = new List<IArtifactTypeHandler>();
        }

        /// <summary>
        /// Registers a new <see cref="IArtifactRepository"/>.
        /// </summary>
        /// <param name="factory">The factory to register.</param>
        public void Register( IArtifactTypeHandler factory )
        {
            if( factory == null ) throw new ArgumentNullException( nameof( factory ) );
            if( _typeHandlers.Contains( factory ) ) throw new InvalidOperationException( nameof( factory ) );
            _typeHandlers.Add( factory );
        }

        /// <summary>
        /// Ensure that source feeds are created from a set of xml elements.
        /// </summary>
        /// <param name="readers">Set of readers.</param>
        public void InstanciateFeeds( IEnumerable<XElementReader> readers )
        {
            foreach( var r in readers ) InstanciateFeed( r );
        }

        IArtifactFeed InstanciateFeed( XElementReader r )
        {
            foreach( var h in _typeHandlers )
            {
                var f = h.CreateFeed( r );
                if( f != null ) return f;
            }
            throw new ArgumentException( "Unable to resolve a package feed for: " + r.Element.ToString() );
        }

        /// <summary>
        /// Finds a <see cref="IArtifactFeed"/> from its <see cref="IArtifactFeed.TypedName"/>.
        /// If no feed can be found, an <see cref="ArgumentException"/> is thrown.
        /// </summary>
        /// <param name="uniqueTypedName">The <see cref="IArtifactFeed.TypedName"/>.</param>
        /// <returns>The source.</returns>
        public IArtifactFeed FindFeed( string uniqueTypedName )
        {
            if( uniqueTypedName == null ) throw new ArgumentNullException( nameof( uniqueTypedName ) );
            foreach( var f in _typeHandlers )
            {
                var r = f.FindFeed( uniqueTypedName );
                if( r != null ) return r;
            }
            throw new ArgumentException( $"Unable to find a feed named '{uniqueTypedName}'." );
        }


        /// <summary>
        /// Creates a <see cref="IArtifactRepositoryInfo"/> from a <see cref="XElement"/>
        /// by calling <see cref="IArtifactTypeHandler.CreateInfo(XElement)"/> on each registered
        /// factories and returning the first one that returns a non null information object.
        /// If no factory can handle the element, an <see cref="ArgumentException"/> is thrown.
        /// <para>
        /// When "CheckName" attribute exists on the element, it must exactly match the
        /// resulting <see cref="IArtifactRepositoryInfo.UniqueArtifactRepositoryName"/> otherwise
        /// an <see cref="ArgumentException"/> is thrown.
        /// </para>
        /// </summary>
        /// <param name="r">The xml reader element.</param>
        /// <returns>The mapped repository info.</returns>
        IArtifactRepositoryInfo ReadRepositoryInfo( in XElementReader r )
        {
            foreach( var f in _typeHandlers )
            {
                var info = f.ReadRepositoryInfo( r );
                if( info != null )
                {
                    var checkName = r.HandleOptionalAttribute<string>( "CheckName", null );
                    if( checkName != null && checkName != info.UniqueArtifactRepositoryName )
                    {
                        throw new ArgumentException( $"Invalid check for name: CheckName is '{checkName}' but the actual repository name is '{info.UniqueArtifactRepositoryName}'." );
                    }
                    var checkSecretKeyName = r.HandleOptionalAttribute<string>( "CheckSecretKeyName", null );
                    if( checkSecretKeyName != null && checkSecretKeyName != info.SecretKeyName )
                    {
                        throw new ArgumentException( $"Invalid check for secret key name: CheckSecretKeyName is '{checkSecretKeyName}' but the actual repository secret key name is '{info.SecretKeyName}'." );
                    }
                    return info;
                }
            }
            throw new ArgumentException( "Unable to map Xml element to an ArtifactRepositoryInfo: " + r.ToString() );
        }

        /// <summary>
        /// Ensure that repositories are created from a set of xml info elements.
        /// </summary>
        /// <param name="readers">Set of readers.</param>
        public void InstanciateRepositories( IEnumerable<XElementReader> readers )
        {
            foreach( var r in readers )
            {
                var info = ReadRepositoryInfo( r );
                InstanciateRepository( r.Monitor, info );
            }
        }

        IArtifactRepository InstanciateRepository( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );
            foreach( var f in _typeHandlers )
            {
                var r = f.FindOrCreate( m, info );
                if( r != null ) return r;
            }
            throw new ArgumentException( "Unable to resolve a repository for repository info: " + info.ToString() );
        }

        /// <summary>
        /// Finds a <see cref="IArtifactRepository"/> from its <see cref="IArtifactRepositoryInfo.UniqueArtifactRepositoryName"/>.
        /// If no repository can be found, an <see cref="ArgumentException"/> is thrown.
        /// </summary>
        /// <param name="uniqueRepositoryName">Repository name.</param>
        /// <returns>The repository.</returns>
        public IArtifactRepository FindRepository( string uniqueRepositoryName )
        {
            if( uniqueRepositoryName == null ) throw new ArgumentNullException( nameof( uniqueRepositoryName ) );
            foreach( var f in _typeHandlers )
            {
                var r = f.FindRepository( uniqueRepositoryName );
                if( r != null ) return r;
            }
            throw new ArgumentException( $"Unable to find a repository named '{uniqueRepositoryName}'." );
        }

        /// <summary>
        /// Attempts to resolve required secrets for a set of <see cref="IArtifactRepository"/>.
        /// If a secret can not be resolved, it will appear as null in the result list.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="repositories">The set of repository for which secrets must be resolved.</param>
        /// <returns>
        /// The list of resolved secrets: a null secret means that the secret has not been successfully obtained
        /// for the corresponding <see cref="IArtifactRepositoryInfo.SecretKeyName"/>.
        /// </returns>
        public IReadOnlyList<(string SecretKeyName, string Secret)> ResolveSecrets( IActivityMonitor m, IEnumerable<IArtifactRepository> repositories )
        {
            return repositories.Where( feed => !String.IsNullOrWhiteSpace( feed.Info.SecretKeyName ) )
                               .GroupBy( feed => feed.Info.SecretKeyName )
                               .Select( g => (g.Key, Secret: g.First().ResolveSecret( m )) )
                               .ToList();
        }
    }
}
