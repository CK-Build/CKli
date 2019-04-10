using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.Env
{
    public class ProtoGitFolder
    {
        protected readonly ISecretKeyStore SecretKeyStore;
        protected readonly CommandRegister CommandRegister;
        public ProtoGitFolder(
            string url,
            string path,
            IWorldName world,
            ISecretKeyStore secretKeyStore,
            FileSystem fileSystem,
            CommandRegister commandRegister)
        {
            if( url != null )
            {
                if( url.IndexOf( "github.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitHub;
                else if( url.IndexOf( "gitlab.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitLab;
                else if( url.IndexOf( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.AzureDevOps;
                else if( url.IndexOf( "bitbucket.org", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.Bitbucket;
            }

            Url = url;
            World = world;
            SecretKeyStore = secretKeyStore;
            FullPhysicalPath = path;
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
        public string Url { get; }

        /// <summary>
        /// Gets the full path (that starts with the <see cref="FileSystem"/>' root path) of the Git folder.
        /// </summary>
        public NormalizedPath FullPhysicalPath { get; }

        public GitFolder Clone( IActivityMonitor m )
        {
            using( m.OpenInfo( $"Checking out '{FullPhysicalPath}' from '{ Url }' on { World.DevelopBranchName }." ) )
            {
                Repository.Clone( Url, FullPhysicalPath, new CloneOptions()
                {
                    CredentialsProvider = (url, user, cred) => PATCredentialsHandler(m, url, user, cred),
                    BranchName = World.DevelopBranchName,
                    Checkout = true
                } );
            }
            return new GitFolder(m, SecretKeyStore, FileSystem, CommandRegister, World, FullPhysicalPath, Url);
        }

        protected Credentials PATCredentialsHandler(IActivityMonitor m, string url, string user, SupportedCredentialTypes cred )
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
            pat = Convert.ToBase64String( Encoding.UTF8.GetBytes( pat ) );
            return new UsernamePasswordCredentials()
            {
                Username = "CK-Env",
                Password = pat
            };
        }

        /// <summary>
        /// Gets the known Git provider.
        /// </summary>
        public KnownGitProvider KnownGitProvider { get; }
    }
}
