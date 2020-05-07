using CK.Core;
using CK.Text;
using System;
using System.IO;

namespace CK.Env
{
    /// <summary>
    /// This UserHost is in charge of a whole CKli context: this hosts the <see cref="WorldStore"/> (based on 'Stacks.xml' file and Git repositories of stacks),
    /// an internal <see cref="SimpleWorldLocalMapping"/> (on 'WorldLocalMapping.txt' file), the <see cref="WorldSelector"/>,
    /// the <see cref="CommandRegister"/> and the <see cref="UserKeyVault"/> (on  "Personal.KeyVault.txt" file).
    /// <para>
    /// This is typically initialized on a <see cref="Environment.SpecialFolder.LocalApplicationData"/>/CKli folder.
    /// </para>
    /// </summary>
    public sealed class UserHost : ICommandMethodsProvider, IDisposable
    {
        public static readonly NormalizedPath HomeCommandPath = "Home";
        public static readonly NormalizedPath WorldCommandPath = "World";

        readonly XTypedFactory _xTypedObjectfactory;
        readonly SimpleWorldLocalMapping _worldMapping;
        readonly GitWorldStore _store;

        UserHost( IBasicApplicationLifetime lifetime, NormalizedPath userHostPath )
        {
            Directory.CreateDirectory( userHostPath );
            ApplicationLifetime = lifetime;
            CommandRegister = new CommandRegister();
            _xTypedObjectfactory = new XTypedFactory();
            UserKeyVault = new FileKeyVault( userHostPath.AppendPart( "Personal.KeyVault.txt" ) );
            _worldMapping = new SimpleWorldLocalMapping( userHostPath.AppendPart( "WorldLocalMapping.txt" ) );
            _store = new GitWorldStore( userHostPath, _worldMapping, UserKeyVault.KeyStore, CommandRegister );
            WorldSelector = new WorldSelector( _store, CommandRegister, _xTypedObjectfactory, UserKeyVault.KeyStore, lifetime );
            CommandRegister.Register( this );
        }

        /// <summary>
        /// Creates a UserHost (that should be disposed).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="lifetime">The lifetime controller.</param>
        /// <param name="userHostPath">The base path that will contain the stack's Git repositories, the user key vault and the world mapping.</param>
        /// <returns>A UserHost.</returns>
        public static UserHost Create( IActivityMonitor m, IBasicApplicationLifetime lifetime, NormalizedPath userHostPath )
        {
            var u = new UserHost( lifetime, userHostPath );
            // Suppresing the previous UserDomainName based name.
            if( !u.UserKeyVault.KeyVaultFileExists )
            {
                var oldKeyVaultName = "CKLI-" + Environment.UserDomainName
                                        .Replace( '-', '_' )
                                        .Replace( '/', '_' )
                                        .Replace( '\\', '_' )
                                        .Replace( '.', '_' )
                                        .ToUpperInvariant();
                var oldPath = userHostPath.AppendPart( oldKeyVaultName + ".KeyVault.txt" );
                if( File.Exists( oldPath ) ) File.Move( oldPath, u.UserKeyVault.KeyVaultPath );
            }
            // First initialization: the key vault is not opened.
            u._xTypedObjectfactory.AutoRegisterFromLoadedAssemblies( m );
            u._store.ReadStacksFromLocalStacksFilePath( m );
            return u;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => HomeCommandPath;

        public CommandRegister CommandRegister { get; }

        public IBasicApplicationLifetime ApplicationLifetime { get; }

        /// <summary>
        /// Gets the world store. <see cref="GitWorldStore.ReadWorlds(IActivityMonitor, bool)"/> should be called
        /// to initialize it after a <see cref="UserKeyVault"/> has been opened: required secrets will automatically be used.
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
        /// Requires <see cref="GitWorldStore.DisableRepositoryAndStacksCommands"/> to be false.
        /// </summary>
        public bool CanEnsureStackRepository => !_store.DisableRepositoryAndStacksCommands;

        /// <summary>
        /// Registers a new <see cref="StackRepo"/> or updates its <see cref="GitRepositoryKey.IsPublic"/> configuration.
        /// This method is exposed as a command here in order to appear in the "Home/" command namespace (instead of the "World/").
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="url">The repository url. Must not be numm or empty.</param>
        /// <param name="isPublic">Whether this repository contains public (Open Source) worlds.</param>
        [CommandMethod]
        public void EnsureStackRepository( IActivityMonitor m, string url, bool isPublic ) => _store.EnsureStackRepository( m, url, isPublic );

        /// <summary>
        /// Requires <see cref="GitWorldStore.DisableRepositoryAndStacksCommands"/> to be false.
        /// </summary>
        public bool CanDeleteStackRepository => !_store.DisableRepositoryAndStacksCommands;

        /// <summary>
        /// Removes a stack repository.
        /// A warning is emitted if the repository is not registered.
        /// This method is exposed as a command here in order to appear in the "Home/" command namespace (instead of the "World/").
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="url">The url of the repository to remove.</param>
        [CommandMethod]
        public void DeleteStackRepository( IActivityMonitor m, string url ) => _store.DeleteStackRepository( m, url );

        public void Dispose()
        {
            WorldSelector.Dispose();
            _store.Dispose();
        }
    }
}
