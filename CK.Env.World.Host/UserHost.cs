using CK.Core;
using CK.Text;
using System;
using System.IO;

namespace CK.Env
{
    public sealed class UserHost : ICommandMethodsProvider, IDisposable
    {
        public static readonly NormalizedPath HomeCommandPath = "Home";
        public static readonly NormalizedPath WorldCommandPath = "World";

        readonly XTypedFactory _xTypedObjectfactory;
        readonly SimpleWorldLocalMapping _worldMapping;
        readonly GitWorldStore _store;

        public UserHost( IBasicApplicationLifetime lifetime, NormalizedPath userHostPath )
        {
            Directory.CreateDirectory( userHostPath );
            ApplicationLifetime = lifetime;
            CommandRegister = new CommandRegister();
            _xTypedObjectfactory = new XTypedFactory();
            UserKeyVault = new UserKeyVault( userHostPath );
            _worldMapping = new SimpleWorldLocalMapping( userHostPath.AppendPart( "WorldLocalMapping.txt" ) );
            _store = new GitWorldStore( userHostPath, _worldMapping, UserKeyVault.KeyStore, CommandRegister );
            WorldSelector = new WorldSelector( _store, CommandRegister, _xTypedObjectfactory, UserKeyVault.KeyStore, lifetime );
            CommandRegister.Register( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => HomeCommandPath;

        public CommandRegister CommandRegister { get; }

        public IBasicApplicationLifetime ApplicationLifetime { get; }

        public GitWorldStore WorldStore => _store;

        public WorldSelector WorldSelector { get; }

        public UserKeyVault UserKeyVault { get; }

        public void Initialize( IActivityMonitor m )
        {
            if( !UserKeyVault.IsKeyVaultOpened )
            {
                // First initialization: the key vault is not opened.
                _xTypedObjectfactory.AutoRegisterFromLoadedAssemblies( m );
                _store.ReadStacksFromLocalStacksFilePath( m );
            }
            else
            {
                _store.ReadWorlds( m, true );
            }
        }

        //[CommandMethod]
        //public void Refresh( IActivityMonitor m )
        //{
        //    _store.ReadWorlds( m );
        //    if( WorldSelector.CanClose )
        //    {
        //        var opened = WorldSelector.CurrentWorld.WorldName.FullName;
        //        if( opened != null )
        //        {
        //            WorldSelector.CloseWorld( m );
        //            WorldSelector.OpenWorld( m, opened );
        //        }
        //    }
        //}

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
