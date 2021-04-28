using CK.SimpleKeyVault;
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
            OriginUrl = CheckAndNormalizeRepositoryUrl( url );
            SecretKeyStore = secretKeyStore ?? throw new ArgumentNullException( nameof( secretKeyStore ) );
            IsPublic = isPublic;

            if( url.Authority.Equals( "github.com", StringComparison.OrdinalIgnoreCase ) ) KnownGitProvider = KnownGitProvider.GitHub;
            else if( url.Authority.Equals( "gitlab.com", StringComparison.OrdinalIgnoreCase ) ) KnownGitProvider = KnownGitProvider.GitLab;
            else if( url.Authority.Equals( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) ) KnownGitProvider = KnownGitProvider.AzureDevOps;
            else if( url.Authority.Equals( "bitbucket.org", StringComparison.OrdinalIgnoreCase ) ) KnownGitProvider = KnownGitProvider.Bitbucket;
            else if( url.Scheme == Uri.UriSchemeFile ) KnownGitProvider = KnownGitProvider.FileSystem;

            if( KnownGitProvider == KnownGitProvider.FileSystem ) return; // No credentials needed.
            if( KnownGitProvider != KnownGitProvider.Unknown )
            {
                string GetReadPATDescription( SecretKeyInfo? current )
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
        /// Normalizes a valid, absolute, url. If the path ends with ".git" (case insensitive), this suffix is removed.
        /// </summary>
        /// <param name="url">The valid, absolute, url.</param>
        /// <returns>The normalized url.</returns>
        public static Uri CheckAndNormalizeRepositoryUrl( Uri url )
        {
            if( url == null ) throw new ArgumentNullException( nameof( url ) );
            if( !url.IsAbsoluteUri ) throw new ArgumentException( $"Invalid Url. It must be absolute: {url}", nameof( url ) );
            if( url.Query.Length == 0 )
            {
                // Security: since execution paths may differ before reaching the constructor, multiple suffix may be handled or not.
                // Here we ensure that all suffixes are removed.
                while( url.AbsolutePath.EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) )
                {
                    var s = url.AbsoluteUri;
                    url = new Uri( s.Remove( s.Length - 4 ) );
                }
            }
            return url;
        }

        /// <summary>
        /// Checks that the 2 urls's string, after a call to <see cref="CheckAndNormalizeRepositoryUrl(Uri)"/>
        /// are <see cref="StringComparison.OrdinalIgnoreCase"/> equal.
        /// This is a lot of computation for a boolean and it is hard to "optimize" since we never know if the urls
        /// have already been normalized.
        /// </summary>
        /// <param name="u1">First valid and absolute url.</param>
        /// <param name="u2">Second valid and absolute url.</param>
        /// <returns>Whether the 2 urls are equivalent.</returns>
        public static bool IsEquivalentRepositoryUri( Uri u1, Uri u2 )
        {
            u1 = CheckAndNormalizeRepositoryUrl( u1 );
            u2 = CheckAndNormalizeRepositoryUrl( u2 );
            return u1.ToString().Equals( u2.ToString(), StringComparison.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Gets whether the Git repository is public.
        /// Specialized classes may set this property.
        /// </summary>
        public bool IsPublic { get; protected set; }

        /// <summary>
        /// Gets the remote origin url.
        /// This is checked in the constructor as an absolute url and if the url has no query part 
        /// and the path ends with ".git", the trailing .git is removed.
        /// See https://stackoverflow.com/a/11069413/. 
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
        public string? ReadPATKeyName { get; }

        /// <summary>
        /// Gets the write PAT key name for this repository.
        /// This PAT must allow pushes to the repository.
        /// </summary>
        public string? WritePATKeyName { get; }

        /// <summary>
        /// Helper that formats the PAT name based on the kind of provider.
        /// <see cref="KnownGitProvider"/> must not be Unknown.
        /// </summary>
        /// <param name="suffix">Suffix to use.</param>
        /// <returns>The PAT name or null if <see cref="KnownGitProvider"/> is Unknown.</returns>
        public string GetPATName( string suffix = "_PAT" )
        {
            switch( KnownGitProvider )
            {
                case KnownGitProvider.Unknown: throw new InvalidOperationException( "Unknown GitProvider." );
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
