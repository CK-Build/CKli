using CK.Core;
using CK.SimpleKeyVault;

using LibGit2Sharp;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env
{
    /// <summary>
    /// Captures all information required to instantiate an actual <see cref="GitRepository"/> in two steps.
    /// This split in two phases is mainly to first collect the secrets required by the
    /// repositories and resolve them before any actual instantiation but also to defer the repository opening.
    /// <para>
    /// This can be created by <see cref="FileSystem.DeclareProtoGitFolder(IWorldName, NormalizedPath, string, bool)"/>.
    /// </para>
    /// </summary>
    public sealed class ProtoGitFolder : GitRepositoryKey
    {
        internal GitRepository? _loaded;

        internal ProtoGitFolder( Uri url,
                                 bool isPublic,
                                 in NormalizedPath folderPath,
                                 IWorldName world,
                                 SecretKeyStore secretKeyStore,
                                 FileSystem fileSystem )
            : base( secretKeyStore, url, isPublic )
        {
            Debug.Assert( !folderPath.IsEmptyPath, "Empty path: FileSystem.Root path can not be a Git folder." );
            Debug.Assert( !folderPath.IsRooted, "Must be relative to the FileSystem.Root." );
            Debug.Assert( !folderPath.EndsWith( ".git" ), "Path should be the repository directory and not the .git directory." );

            Throw.CheckNotNullArgument( world );
            Throw.CheckNotNullArgument( fileSystem );

            World = world;
            FolderPath = folderPath;
            FullPhysicalPath = fileSystem.Root.Combine( folderPath );
            FileSystem = fileSystem;
            PluginRegistry = new GitPluginRegistry( folderPath );
        }

        /// <summary>
        /// Gets the current <see cref="IWorldName"/>.
        /// </summary>
        public IWorldName World { get; }

        /// <summary>
        /// Gets the file system object.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gets the path that is relative to <see cref="FileSystem.Root"/> and contains the .git sub folder.
        /// </summary>
        public NormalizedPath FolderPath { get; }

        /// <summary>
        /// Gets the full path (that starts with the <see cref="FileSystem"/>' root path) of the Git folder.
        /// </summary>
        public NormalizedPath FullPhysicalPath { get; }

        /// <summary>
        /// Gets the plugin registry for this repository.
        /// </summary>
        public GitPluginRegistry PluginRegistry { get; }

        /// <summary>
        /// Gets the repository if it has been <see cref="Load(IActivityMonitor)"/>.
        /// </summary>
        public GitRepository? Loaded => _loaded;

        /// <summary>
        /// Ensures that the <see cref="GitRepository"/> is loaded.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The <see cref="GitRepository"/> or null on error.</returns>
        public GitRepository? Load( IActivityMonitor monitor )
        {
            if( _loaded == null ) DoLoad( monitor );
            return _loaded;
        }

        [MemberNotNullWhen( true, nameof( _loaded ) )]
        internal bool DoLoad( IActivityMonitor monitor )
        {
            Debug.Assert( _loaded == null );
            var r = GitRepositoryBase.EnsureWorkingFolder( monitor, this, FullPhysicalPath, World.DevelopBranchName );
            if( r == null ) return false;
            var g = new GitRepository( r, this );
            _loaded = g;
            FileSystem.OnLoaded( g );
            return true;
        }

        /// <summary>
        /// Binds this <see cref="ProtoGitFolder"/> to the static  <see cref="GitRepositoryBase.PATCredentialsHandler(IActivityMonitor, GitRepositoryKey)"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The Credentials object that is null or a <see cref="UsernamePasswordCredentials"/>.</returns>
        internal Credentials? PATCredentialsHandler( IActivityMonitor m ) => GitRepositoryBase.PATCredentialsHandler( m, this );

    }
}
