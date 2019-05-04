using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace CK.Env
{
    public class ProtoGitFolder
    {
        public ProtoGitFolder(
            string url,
            string fullPhysicalPath,
            IWorldName world,
            ISecretKeyStore secretKeyStore,
            FileSystem fileSystem,
            CommandRegister commandRegister )
        {
            if( url == null ) throw new ArgumentNullException( nameof( url ) );
            if( fullPhysicalPath == null ) throw new ArgumentNullException( nameof( fullPhysicalPath ) );
            if( fullPhysicalPath.Contains( ".git" ) ) throw new ArgumentException( "Path should be the repository directory and not the .git directory" );
            if( world == null ) throw new ArgumentNullException( nameof( world ) );
            if( secretKeyStore == null ) throw new ArgumentNullException( nameof( secretKeyStore ) );
            if( fileSystem == null ) throw new ArgumentNullException( nameof( fileSystem ) );
            if( commandRegister == null ) throw new ArgumentNullException( nameof( commandRegister ) );

            if( url.IndexOf( "github.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitHub;
            else if( url.IndexOf( "gitlab.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitLab;
            else if( url.IndexOf( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.AzureDevOps;
            else if( url.IndexOf( "bitbucket.org", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.Bitbucket;

            OriginUrl = url;
            World = world;
            SecretKeyStore = secretKeyStore;
            FullPhysicalPath = fullPhysicalPath;
            FileSystem = fileSystem;
            CommandRegister = commandRegister;
        }

        /// <summary>
        /// Gets the current <see cref="IWorldName"/>.
        /// </summary>
        public IWorldName World { get; }

        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gets the current remote origin url.
        /// </summary>
        public string OriginUrl { get; }

        /// <summary>
        /// Gets the known Git provider.
        /// </summary>
        public KnownGitProvider KnownGitProvider { get; }

        /// <summary>
        /// Gets the full path (that starts with the <see cref="FileSystem"/>' root path) of the Git folder.
        /// </summary>
        public NormalizedPath FullPhysicalPath { get; }

        public ISecretKeyStore SecretKeyStore { get; }

        public CommandRegister CommandRegister { get; }

        public GitFolder Ensure( IActivityMonitor m )
        {
            var gitFolderPath = Path.Combine( FullPhysicalPath, ".git" );
            if( !Directory.Exists( gitFolderPath ) )
            {
                using( m.OpenInfo( $"Checking out '{FullPhysicalPath}' from '{OriginUrl}' on {World.DevelopBranchName}." ) )
                {
                    Repository.Clone( OriginUrl, FullPhysicalPath, new CloneOptions()
                    {
                        CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( m, url ),
                        BranchName = World.DevelopBranchName,
                        Checkout = true
                    } );
                }
            }
            else if( !Repository.IsValid( gitFolderPath ) )
            {
                throw new InvalidOperationException( $"Git folder {gitFolderPath} exists but is not a valid Repository" );
            }
            return new GitFolder( this );
        }

        public Credentials PATCredentialsHandler( IActivityMonitor m, string url )
        {
            string keyName;
            switch( KnownGitProvider )
            {
                case KnownGitProvider.AzureDevOps:
                    var regex = Regex.Match( url, @"(?:\:\/\/)[^\/]*\/([^\/]*)" );
                    string organization = regex.Groups[1].Value;
                    keyName = "AZURE_GIT_" + organization
                        .ToUpperInvariant()
                        .Replace( '-', '_' )
                        .Replace( ' ', '_' )
                        + "_PAT";
                    break;
                default:
                    keyName = KnownGitProvider.ToString() + "_GIT_PAT";
                    break;
            }
            string pat = SecretKeyStore.GetSecretKey( m, keyName, true );
            return new UsernamePasswordCredentials()
            {
                Username = "CK-Env",
                Password = pat
            };
        }

    }
}
