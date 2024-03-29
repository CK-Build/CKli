using CK.Core;
using CK.SimpleKeyVault;

using System;
using System.IO;

namespace CK.Env
{
    /// <summary>
    /// This SingleWorldHome handles one single world.
    /// <para>
    /// This is typically initialized on a <see cref="Environment.SpecialFolder.LocalApplicationData"/>/CKli folder.
    /// </para>
    /// </summary>
    public sealed class SingleWorldHome
    {
        public static readonly NormalizedPath HomeCommandPath = "Home";
        public static readonly NormalizedPath WorldCommandPath = "World";

        readonly XTypedFactory _xTypedObjectfactory;
        readonly GitWorldStore _store;
        readonly WorldBearer _worldBearer;

        SingleWorldHome( NormalizedPath userHostPath, XTypedFactory baseFactory, IRootedWorldName singleWorld, SecretKeyStore keyStore, Func<IReleaseVersionSelector> releaseVersionSelectorFatory )
        {
            Directory.CreateDirectory( userHostPath );
            CommandRegister = new CommandRegister();
            _xTypedObjectfactory = new XTypedFactory( baseFactory );
            _store = new GitWorldStore( userHostPath, singleWorld, keyStore );
            _worldBearer = new WorldBearer();
        }

        /// <summary>
        /// Creates a MultipleWorldHome (that should be disposed).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="rootStorePath">The base path that will contain the stack's Git repositories, the user key vault and the world mapping.</param>
        /// <param name="sharedFactory">Shared xml to object factory. It is typically <see cref="XTypedFactory.IsLocked"/>.</param>
        /// <param name="singleWorld">Single world.</param>
        /// <param name="keyStore">Key store to use.</param>
        /// <param name="releaseVersionSelectorFactory">Factory for <see cref="IReleaseVersionSelector"/>. One of them will be created for each world.</param>
        /// <returns>A SingleWorldHome or null on error.</returns>
        public static SingleWorldHome? Create( IActivityMonitor m,
                                               NormalizedPath rootStorePath,
                                               XTypedFactory sharedFactory,
                                               IRootedWorldName singleWorld,
                                               SecretKeyStore keyStore,
                                               Func<IReleaseVersionSelector> releaseVersionSelectorFatory )
        {
            var u = new SingleWorldHome( rootStorePath, sharedFactory, singleWorld, keyStore, releaseVersionSelectorFatory );
            if( !u._store.ReadStacksFromLocalStacksFilePath( m )
                || !u._store.StackRepositories[0].RefreshSingle( m, out var name )
                || !u._worldBearer.OpenWorld( m, name, new XTypedFactory( sharedFactory ), u.CommandRegister, u.WorldStore, keyStore, releaseVersionSelectorFatory ) )
            {
                return null;
            }
            return u;
        }

        public CommandRegister CommandRegister { get; }

        /// <summary>
        /// Gets the world store.
        /// </summary>
        public GitWorldStore WorldStore => _store;

        /// <summary>
        /// Gets the world.
        /// </summary>
        public World World => _worldBearer.World!;

        /// <summary>
        /// Disposes this <see cref="MultipleWorldHome"/> by closing the opened world and
        /// disposing the store.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        public void Dispose( IActivityMonitor monitor )
        {
            _worldBearer.Close( monitor );
            _store.Dispose();
        }
    }
}
