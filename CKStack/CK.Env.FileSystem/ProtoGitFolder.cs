using CK.Core;
using CK.SimpleKeyVault;

using LibGit2Sharp;
using System;

namespace CK.Env
{
    /// <summary>
    /// Captures all information required to instantiate an actual <see cref="GitRepository"/> in two steps.
    /// This split in two phases is mainly to first collect the secrets required by the
    /// repositories and resolve them before any actual instantiation.
    /// </summary>
    public sealed class ProtoGitFolder : GitRepositoryKey
    {
        /// <summary>
        /// Initializes a new <see cref="ProtoGitFolder"/>.
        /// </summary>
        /// <param name="url">The url of the remote.</param>
        /// <param name="isPublic">Whether this repository is public.</param>
        /// <param name="folderPath">The path that is relative to <see cref="FileSystem.Root"/> and contains the .git sub folder.</param>
        /// <param name="world">The world name.</param>
        /// <param name="secretKeyStore">The secret key store.</param>
        /// <param name="fileSystem">=The file system.</param>
        /// <param name="commandRegister">The command register.</param>
        public ProtoGitFolder( Uri url,
                               bool isPublic,
                               in NormalizedPath folderPath,
                               IWorldName world,
                               SecretKeyStore secretKeyStore,
                               FileSystem fileSystem,
                               CommandRegister commandRegister )
            : base( secretKeyStore, url, isPublic )
        {
            Throw.CheckArgument( "Empty path: FileSystem.Root path can not be a Git folder.", !folderPath.IsEmptyPath );
            Throw.CheckArgument( "Must be relative to the FileSystem.Root.", !folderPath.IsRooted );
            Throw.CheckArgument( "Path should be the repository directory and not the .git directory.", !folderPath.EndsWith( ".git" ) );

            Throw.CheckNotNullArgument( world );
            Throw.CheckNotNullArgument( fileSystem );
            Throw.CheckNotNullArgument( commandRegister );

            World = world;
            FolderPath = folderPath;
            FullPhysicalPath = fileSystem.Root.Combine( folderPath );
            FileSystem = fileSystem;
            CommandRegister = commandRegister;
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
        /// Gets the command register.
        /// </summary>
        public CommandRegister CommandRegister { get; }

        /// <summary>
        /// Gets the plugin registry for this repository.
        /// </summary>
        public GitPluginRegistry PluginRegistry { get; }

        /// <summary>
        /// Ensures that the Git working folder actually exists and creates a
        /// GitFolder instance where the checked out or returns null on error.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The GitFolder instance or null on error.</returns>
        public GitRepository? CreateGitFolder( IActivityMonitor m )
        {
            var r = GitRepositoryBase.EnsureWorkingFolder( m, this, FullPhysicalPath, true, World.DevelopBranchName );
            return r != null ? new GitRepository( r, this ) : null;
        }

        /// <summary>
        /// Binds this <see cref="ProtoGitFolder"/> to the static  <see cref="GitRepositoryBase.PATCredentialsHandler(IActivityMonitor, GitRepositoryKey)"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The Credentials object that is null or a <see cref="UsernamePasswordCredentials"/>.</returns>
        internal Credentials? PATCredentialsHandler( IActivityMonitor m ) => GitRepositoryBase.PATCredentialsHandler( m, this );

    }
}
