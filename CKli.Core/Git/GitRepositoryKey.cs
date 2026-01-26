using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace CKli.Core;

/// <summary>
/// Encapsulates <see cref="KnownGitProvider"/> lookup from a repository url and
/// <see cref="ReadPATKeyName"/> and <see cref="WritePATKeyName"/> formatting.
/// </summary>
public partial class GitRepositoryKey
{
    sealed class UriComparer : IEqualityComparer<Uri>
    {
        public bool Equals( Uri? x, Uri? y ) => StringComparer.OrdinalIgnoreCase.Equals( x?.ToString(), y?.ToString() );

        public int GetHashCode( [DisallowNull] Uri obj ) => StringComparer.OrdinalIgnoreCase.GetHashCode( obj.ToString() );
    }

    /// <summary>
    /// An equality comparer for Url that uses <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public static readonly IEqualityComparer<Uri> OrdinalIgnoreCaseUrlEqualityComparer = new UriComparer();

    readonly Uri _originUrl;
    readonly ISecretsStore _secretsStore;
    readonly KnownCloudGitProvider _knownGitProvider;
    readonly bool _isPublic;

    string? _prefixPAT;
    string? _readPATKeyName;
    UsernamePasswordCredentials? _readCreds;
    string? _writePATKeyName;
    UsernamePasswordCredentials? _writeCreds;

    /// <summary>
    /// Initializes a new <see cref="GitRepositoryKey"/>.
    /// </summary>
    /// <param name="secretsStore">The secrets store to use.</param>
    /// <param name="url">The url of the remote.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    public GitRepositoryKey( ISecretsStore secretsStore, Uri url, bool isPublic )
    {
        _originUrl = CheckAndNormalizeRepositoryUrl( url );
        _secretsStore = secretsStore;
        _isPublic = isPublic;

        if( url.Authority.Equals( "github.com", StringComparison.OrdinalIgnoreCase ) ) _knownGitProvider = KnownCloudGitProvider.GitHub;
        else if( url.Authority.Equals( "gitlab.com", StringComparison.OrdinalIgnoreCase ) ) _knownGitProvider = KnownCloudGitProvider.GitLab;
        else if( url.Authority.Equals( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) ) _knownGitProvider = KnownCloudGitProvider.AzureDevOps;
        else if( url.Authority.Equals( "bitbucket.org", StringComparison.OrdinalIgnoreCase ) ) _knownGitProvider = KnownCloudGitProvider.Bitbucket;
        else if( url.Scheme == Uri.UriSchemeFile ) _knownGitProvider = KnownCloudGitProvider.FileSystem;
    }

    /// <summary>
    /// Gets whether the Git repository is public.
    /// </summary>
    public bool IsPublic => _isPublic;

    /// <summary>
    /// Gets the remote origin url.
    /// This is checked in the constructor as an absolute url and if the url has no query part 
    /// and the path ends with ".git", the trailing .git is removed.
    /// See https://stackoverflow.com/a/11069413/. 
    /// </summary>
    public Uri OriginUrl => _originUrl;

    /// <summary>
    /// Gets the known Git provider.
    /// </summary>
    public KnownCloudGitProvider KnownGitProvider => _knownGitProvider;

    /// <summary>
    /// Tries to get the credentials to be able to read the remote repository.
    /// This is always successful and <paramref name="creds"/> is null when <see cref="IsPublic"/> is true.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="creds">The credentials to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool GetReadCredentials( IActivityMonitor monitor, out UsernamePasswordCredentials? creds )
    {
        if( _readCreds == null )
        {
            if( _isPublic )
            {
                creds = null;
                return true;
            }
            var pat = _secretsStore.TryGetRequiredSecret( monitor, WritePATKeyName, ReadPATKeyName );
            _readCreds = pat != null
                            ? new UsernamePasswordCredentials() { Username = "CKli", Password = pat }
                            : null;
        }
        creds = _readCreds;
        return creds != null;
    }

    /// <summary>
    /// Tries to get the credentials to be able to push to the remote repository.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="creds">The credentials to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool GetWriteCredentials( IActivityMonitor monitor, [NotNullWhen(true)]out UsernamePasswordCredentials? creds )
    {
        if( _writeCreds == null )
        {
            var pat = _secretsStore.TryGetRequiredSecret( monitor, WritePATKeyName );
            _writeCreds = pat != null
                            ? new UsernamePasswordCredentials() { Username = "CKli", Password = pat }
                            : null;
        }
        creds = _writeCreds;
        return creds != null;
    }

    /// <summary>
    /// Gets the basic, read/clone, PAT key name for this repository.
    /// This PAT is required only if the repository is not public.
    /// </summary>
    public string ReadPATKeyName => _readPATKeyName ??= PrefixPAT + "_READ_PAT";

    /// <summary>
    /// Gets the write PAT key name for this repository.
    /// This PAT must allow pushes to the repository.
    /// </summary>
    public string WritePATKeyName => _writePATKeyName ??= PrefixPAT + "_WRITE_PAT";

    /// <summary>
    /// Common PAT prefix built from <see cref="KnownGitProvider"/> and <see cref="OriginUrl"/>.
    /// </summary>
    public string PrefixPAT
    {
        get
        {
            if( _prefixPAT == null )
            {
                switch( KnownGitProvider )
                {
                    case KnownCloudGitProvider.Unknown:
                        Regex badChars = BadPATChars();
                        string key = badChars.Replace( OriginUrl.Host + "_", "_" );
                        key = key.ToUpperInvariant();
                        if( !key.EndsWith( "_GIT" ) ) key += "_GIT";
                        _prefixPAT = key;
                        break;
                    case KnownCloudGitProvider.AzureDevOps:
                        var regex = AzureDevOps().Match( OriginUrl.PathAndQuery );
                        string organization = regex.Groups[1].Value;
                        _prefixPAT = "AZURE_GIT_" + organization
                                        .ToUpperInvariant()
                                        .Replace( '-', '_' )
                                        .Replace( ' ', '_' );
                        break;
                    default:
                        _prefixPAT = KnownGitProvider.ToString().ToUpperInvariant() + "_GIT";
                        break;
                }
            }
            return _prefixPAT;
        }
    }

    [GeneratedRegex( "[^A-Za-z_0-9]" )]
    private static partial Regex BadPATChars();
    [GeneratedRegex( @"/([^\/]*)" )]
    private static partial Regex AzureDevOps();

    /// <summary>
    /// Overridden to return the OriginUrl. 
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => _originUrl.ToString();

    /// <summary>
    /// Normalizes a valid, absolute, url. If the path ends with ".git" (case insensitive), this suffix is removed.
    /// Throws a <see cref="ArgumentException"/> if <see cref="Uri.IsAbsoluteUri"/> is false.
    /// </summary>
    /// <param name="url">The valid, absolute, url.</param>
    /// <returns>The normalized url.</returns>
    public static Uri CheckAndNormalizeRepositoryUrl( Uri url )
    {
        Throw.CheckNotNullArgument( url );
        if( !url.IsAbsoluteUri || url.Query.Length > 0 )
        {
            Throw.ArgumentException( nameof( url ), $"Invalid Url: '{url}' must be absolute and have no query part." );
        }
        // Ensure that all suffixes are removed.
        while( url.AbsolutePath.EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) )
        {
            var s = url.AbsoluteUri;
            url = new Uri( s.Remove( s.Length - 4 ) );
        }
        return url;
    }

    /// <summary>
    /// Normalizes a repository url (see <see cref="CheckAndNormalizeRepositoryUrl(Uri)"/>) and extracts the
    /// repository name that must be valid (see <see cref="WorldName.IsValidRepositoryName"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="url">Any url (can be null).</param>
    /// <param name="repositoryFromUrl">The valid repository name extracted from the url on success.</param>
    /// <returns>The normalized url or null on error.</returns>
    public static Uri? CheckAndNormalizeRepositoryUrl( IActivityMonitor monitor, Uri? url, out string? repositoryFromUrl )
    {
        repositoryFromUrl = null;
        if( url == null || !url.IsAbsoluteUri || url.Query.Length > 0 )
        {
            monitor.Error( $"Invalid Url: '{url}' must be absolute and have no query part." );
            return null;
        }
        // Ensure that all suffixes are removed.
        string absolutePath = url.AbsolutePath;
        while( absolutePath.EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) )
        {
            var s = url.AbsoluteUri;
            url = new Uri( s.Remove( s.Length - 4 ) );
            absolutePath = url.AbsolutePath;
        }
        // Consider that all Git providers follow this pattern: the last part of the path is the repository name.
        // If this must be tweaked for some providers (like the "no query" restriction above), we are at the
        // right place to exploit the KnownGitProvider enum...
        var n = System.IO.Path.GetFileName( absolutePath.AsSpan() );
        if( !WorldName.IsValidRepositoryName( n ) )
        {
            monitor.Error( $"Invalid repository Url: the last path part must be a valid repository name: '{url}'." );
            return null;
        }
        repositoryFromUrl = new string( n );
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
        return OrdinalIgnoreCaseUrlEqualityComparer.Equals( u1, u2 );
    }

}
