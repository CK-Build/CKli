using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.IO;

namespace CK.Env
{
    /// <summary>
    /// This MultipleWorldHome is in charge of a whole CKli context: this hosts the <see cref="WorldStore"/> (based on 'Stacks.xml' file
    /// and Git repositories of stacks), an internal <see cref="FileWorldLocalMapping"/> (on 'WorldLocalMapping.txt' file),
    /// the <see cref="WorldSelector"/>, the <see cref="CommandRegister"/> and the <see cref="UserKeyVault"/> (on  "Personal.KeyVault.txt" file).
    /// <para>
    /// This is typically initialized on a <see cref="Environment.SpecialFolder.LocalApplicationData"/>/CKli folder.
    /// </para>
    /// </summary>
    public sealed class MultipleWorldHome : ICommandMethodsProvider
    {
        public static readonly NormalizedPath HomeCommandPath = "Home";
        public static readonly NormalizedPath WorldCommandPath = "World";

        readonly XTypedFactory _xTypedObjectfactory;
        readonly IWorldLocalMapping _worldMapping;
        readonly GitWorldStore _store;

        MultipleWorldHome( NormalizedPath userHostPath, XTypedFactory baseFactory, FileKeyVault userKeyVault, IWorldLocalMapping mapping, Func<IReleaseVersionSelector> releaseVersionSelectorFatory )
        {
            Directory.CreateDirectory( userHostPath );
            CommandRegister = new CommandRegister();
            _xTypedObjectfactory = new XTypedFactory( baseFactory );
            UserKeyVault = userKeyVault;
            _worldMapping = mapping;
            _store = new GitWorldStore( userHostPath, _worldMapping, UserKeyVault.KeyStore, CommandRegister );
            WorldSelector = new WorldSelector( _store, CommandRegister, _xTypedObjectfactory, UserKeyVault.KeyStore, releaseVersionSelectorFatory );
            CommandRegister.Register( this );
        }

        /// <summary>
        /// Creates a MultipleWorldHome (that should be disposed).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="userHostPath">The base path that will contain the stack's Git repositories, the user key vault and the world mapping.</param>
        /// <param name="sharedFactory">Shared xml to object factory. It is typically <see cref="XTypedFactory.IsLocked"/>.</param>
        /// <param name="mapping">World local mapping.</param>
        /// <param name="releaseVersionSelectorFactory">Factory for <see cref="IReleaseVersionSelector"/>. One of them will be created for each world.</param>
        /// <returns>A UserHost.</returns>
        public static MultipleWorldHome Create( IActivityMonitor m, NormalizedPath userHostPath, XTypedFactory sharedFactory, FileKeyVault userKeyVault, IWorldLocalMapping mapping, Func<IReleaseVersionSelector> releaseVersionSelectorFatory )
        {
            var u = new MultipleWorldHome( userHostPath, sharedFactory, userKeyVault, mapping, releaseVersionSelectorFatory );
            u._store.ReadStacksFromLocalStacksFilePath( m );
            u._store.ReadWorlds( m, withPull: true );
            return u;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => HomeCommandPath;

        public CommandRegister CommandRegister { get; }

        /// <summary>
        /// Gets the world store.
        /// </summary>
        public GitWorldStore WorldStore => _store;

        /// <summary>
        /// Handles the selection of one active <see cref="WorldSelector.CurrentWorld"/> among the ones of the <see cref="WorldStore"/>.
        /// </summary>
        public WorldSelector WorldSelector { get; }

        /// <summary>
        /// Handles the key vault personal file.
        /// </summary>
        public FileKeyVault UserKeyVault { get; }

        /// <summary>
        /// Requires the <see cref="GitWorldStore.CanEnsureStackRepository"/> to be true.
        /// </summary>
        public bool CanEnsureStackRepository => _store.CanEnsureStackRepository;

        /// <summary>
        /// Registers a new <see cref="StackRepo"/> or updates its <see cref="GitRepositoryKey.IsPublic"/> configuration.
        /// This method is exposed as a command here in order to appear in the "Home/" command namespace (instead of the "World/").
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="url">The repository url. Must not be null or empty.</param>
        /// <param name="isPublic">Whether this repository contains public (Open Source) worlds.</param>
        [CommandMethod]
        public void EnsureStackRepository( IActivityMonitor monitor, string url, bool isPublic ) => _store.EnsureStackRepository( monitor, url, isPublic );

        /// <summary>
        /// Requires <see cref="GitWorldStore.CanDeleteStackRepository"/> to be true.
        /// </summary>
        public bool CanDeleteStackRepository => _store.CanDeleteStackRepository;

        /// <summary>
        /// Removes a stack repository.
        /// A warning is emitted if the repository is not registered.
        /// This method is exposed as a command here in order to appear in the "Home/" command namespace (instead of the "World/").
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="url">The url of the repository to remove.</param>
        [CommandMethod]
        public void DeleteStackRepository( IActivityMonitor monitor, string url ) => _store.DeleteStackRepository( monitor, url );

        /// <summary>
        /// Disposes this <see cref="MultipleWorldHome"/> by closing the opened world and
        /// disposing the store.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        public void Dispose( IActivityMonitor monitor )
        {
            if( WorldSelector.CanCloseWorld ) WorldSelector.CloseWorld( monitor );
            _store.Dispose();
        }
    }
}
