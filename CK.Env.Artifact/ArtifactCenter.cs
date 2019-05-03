using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using CK.Core;

namespace CK.Env
{
    /// <summary>
    /// Combination of <see cref="IArtifactRepositoryFactory"/>.
    /// Actual management of repositories and infos are totally delegated to the type factories, this
    /// object doesn't track repositories or repository infos.
    /// </summary>
    public class ArtifactCenter
    {
        readonly List<IArtifactRepositoryFactory> _factories;

        public ArtifactCenter()
        {
            _factories = new List<IArtifactRepositoryFactory>();
        }

        /// <summary>
        /// Registers a new <see cref="IArtifactRepository"/>.
        /// </summary>
        /// <param name="factory">The factory to register.</param>
        public void Add( IArtifactRepositoryFactory factory )
        {
            if( factory == null ) throw new ArgumentNullException( nameof( factory ) );
            if( factory == this ) throw new ArgumentException( nameof( factory ) );
            if( _factories.Contains( factory ) ) throw new InvalidOperationException( nameof( factory ) );
            _factories.Add( factory );
        }

        /// <summary>
        /// Creates a <see cref="IArtifactRepositoryInfo"/> from a <see cref="XElement"/>
        /// by calling <see cref="IArtifactRepositoryFactory.CreateInfo(XElement)"/> on each registered
        /// factories and returning the first one that returns a non null information object.
        /// If no factory can handle the element, an <see cref="ArgumentException"/> is thrown.
        /// <para>
        /// When "CheckName" attribute exists on the element, it must exactly match the
        /// resulting <see cref="IArtifactRepositoryInfo.UniqueArtifactRepositoryName"/> otherwise
        /// an <see cref="ArgumentException"/> is thrown.
        /// </para>
        /// </summary>
        /// <param name="e">The xml element.</param>
        /// <returns>The mapped repository info.</returns>
        IArtifactRepositoryInfo CreateInfo( XElement e )
        {
            if( e == null ) throw new ArgumentNullException( nameof( e ) );
            foreach( var f in _factories )
            {
                var info = f.CreateInfo( e );
                if( info != null )
                {
                    var checkName = (string)e.Attribute( "CheckName" );
                    if( checkName != null
                        && checkName != info.UniqueArtifactRepositoryName )
                    {
                        throw new ArgumentException( $"Invalid check for name: CheckName is '{checkName}' but the actual repository name is '{info.UniqueArtifactRepositoryName}'." );
                    }
                    var checkSecretKeyName = (string)e.Attribute( "CheckSecretKeyName" );
                    if( checkSecretKeyName != null
                        && checkSecretKeyName != info.SecretKeyName )
                    {
                        throw new ArgumentException( $"Invalid check for secret key name: CheckSecretKeyName is '{checkSecretKeyName}' but the actual repository secret key name is '{info.SecretKeyName}'." );
                    }
                    return info;
                }
            }
            throw new ArgumentException( "Unable to map Xml element to an ArtifactRepositoryInfo: " + e.ToString() );
        }

        /// <summary>
        /// Ensure that repositories are created from a set of xml info elements.
        /// </summary>
        /// <param name="xmlInfos">The info elements.</param>
        public void InstanciateRepositories( IActivityMonitor m, IEnumerable<XElement> xmlInfos )
        {
            foreach( var xInfo in xmlInfos )
            {
                var info = CreateInfo( xInfo );
                FindOrCreate( m, info );
            }
        }

        /// <summary>
        /// Finds or creates a <see cref="IArtifactRepository"/> from a <see cref="IArtifactRepositoryInfo"/>.
        /// If no registered <see cref="IArtifactRepositoryFactory"/> can provide a repository, an <see cref="ArgumentException"/>
        /// is thrown.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="info">The info that describes the repository.</param>
        /// <returns>A new or existing repository.</returns>
        IArtifactRepository FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );
            foreach( var f in _factories )
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
        public IArtifactRepository Find( string uniqueRepositoryName )
        {
            if( uniqueRepositoryName == null ) throw new ArgumentNullException( nameof( uniqueRepositoryName ) );
            foreach( var f in _factories )
            {
                var r = f.Find( uniqueRepositoryName );
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
