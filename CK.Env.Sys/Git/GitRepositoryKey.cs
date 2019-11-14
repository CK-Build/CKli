using System;
using System.Text.RegularExpressions;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates <see cref="KnownGitProvider"/> lookup from a repository url,
    /// <see cref="ReadPATKeyName"/> and <see cref="WritePATKeyName"/> formatting
    /// and declaration in the <see cref="SecretKeyStore"/>.
    /// </summary>
    public class GitRepositoryKey
    {
        /// <summary>
        /// Initializes a new <see cref="GitRepositoryKey"/>.
        /// </summary>
        /// <param name="secretKeyStore">The secret key store.</param>
        /// <param name="url">The url of the remote.</param>
        /// <param name="isPublic">Whether this repository is public.</param>
        public GitRepositoryKey( SecretKeyStore secretKeyStore, Uri url, bool isPublic )
        {
            if( url == null ) throw new ArgumentNullException( nameof( url ) );
            if( secretKeyStore == null ) throw new ArgumentNullException( nameof( secretKeyStore ) );
            IsPublic = isPublic;
            OriginUrl = url;
            SecretKeyStore = secretKeyStore;

            if( url.Authority.Equals( "github.com", StringComparison.OrdinalIgnoreCase ) ) KnownGitProvider = KnownGitProvider.GitHub;
            else if( url.Authority.Equals( "gitlab.com", StringComparison.OrdinalIgnoreCase ) ) KnownGitProvider = KnownGitProvider.GitLab;
            else if( url.Authority.Equals( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) ) KnownGitProvider = KnownGitProvider.AzureDevOps;
            else if( url.Authority.Equals( "bitbucket.org", StringComparison.OrdinalIgnoreCase ) ) KnownGitProvider = KnownGitProvider.Bitbucket;
            else if( url.Scheme == Uri.UriSchemeFile ) KnownGitProvider = KnownGitProvider.FileSystem;

            if( KnownGitProvider == KnownGitProvider.FileSystem ) return; // No credentials needed.
            if( KnownGitProvider != KnownGitProvider.Unknown )
            {
                string GetReadPATDescription( SecretKeyInfo current )
                {
                    var d = current?.Description ?? $"Used to read/clone private repositories hosted by '{KnownGitProvider}'.";
                    if( (current == null || !current.IsRequired) && !IsPublic )
                    {
                        d += $" This secret is required since at least '{url}' is not public.";
                    }
                    return d;
                }

                // The read PAT is required only if the repository is not public.
                ReadPATKeyName = GetPATName();
                var read = secretKeyStore.DeclareSecretKey( ReadPATKeyName, GetReadPATDescription, isRequired: !IsPublic );
                // The write PAT is the super key of the read PAT.
                WritePATKeyName = GetPATName( "_WRITE_PAT" );
                secretKeyStore.DeclareSecretKey( WritePATKeyName, current => current?.Description ?? $"Used to push solutions hosted by '{KnownGitProvider}'. This is required to publish builds.", subKey: read );
            }
        }

        /// <summary>
        /// Gets whether the Git repository is public.
        /// Specialized classes may set this property.
        /// </summary>
        public bool IsPublic { get; protected set; }

        /// <summary>
        /// Gets the remote origin url.
        /// </summary>
        public Uri OriginUrl { get; }

        /// <summary>
        /// Gets the known Git provider.
        /// </summary>
        public KnownGitProvider KnownGitProvider { get; }

        /// <summary>
        /// Gets the secret key store.
        /// </summary>
        public SecretKeyStore SecretKeyStore { get; }

        /// <summary>
        /// Gets the basic, read/clone, PAT key name for this repository.
        /// Note that if <see cref="IsPublic"/> is true, this PAT should be useless: anyone should be able to
        /// read/clone the repository.
        /// </summary>
        public string ReadPATKeyName { get; }

        /// <summary>
        /// Gets the write PAT key name for this repository.
        /// This PAT must allow pushes to the repository.
        /// </summary>
        public string WritePATKeyName { get; }

        /// <summary>
        /// Helper that formats the PAT name based on the kind of provider.
        /// </summary>
        /// <param name="suffix">Suffix to use.</param>
        /// <returns>The PAT name or null if <see cref="KnownGitProvider"/> is Unknown.</returns>
        public string GetPATName( string suffix = "_PAT" )
        {
            switch( KnownGitProvider )
            {
                case KnownGitProvider.Unknown: return null;
                case KnownGitProvider.AzureDevOps:
                    var regex = Regex.Match( OriginUrl.PathAndQuery, @"/([^\/]*)" );
                    string organization = regex.Groups[1].Value;
                    return "AZURE_GIT_" + organization
                                .ToUpperInvariant()
                                .Replace( '-', '_' )
                                .Replace( ' ', '_' )
                                + suffix;
                default:
                    return KnownGitProvider.ToString().ToUpperInvariant() + "_GIT" + suffix;
            }
        }

        public override string ToString() => $"{OriginUrl} ({(IsPublic ? "Public" : "Private" )})";
    }

}
