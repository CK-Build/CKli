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
public partial class GitRepositoryKey : IGitRepositoryAccessKey
{
    sealed class UriComparer : IEqualityComparer<Uri>
    {
        public bool Equals( Uri? x, Uri? y ) => StringComparer.OrdinalIgnoreCase.Equals( x?.ToString(), y?.ToString() );

        public int GetHashCode( [DisallowNull] Uri obj ) => StringComparer.OrdinalIgnoreCase.GetHashCode( obj.ToString() );
    }

    /// <summary>
    /// The <see cref="PrefixPAT"/> used for <see cref="KnownCloudGitProvider.FileSystem"/>.
    /// </summary>
    public const string FileSystemPrefixPAT = "FILESYSTEM_GIT";

    /// <summary>
    /// An equality comparer for Url that uses <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public static readonly IEqualityComparer<Uri> OrdinalIgnoreCaseUrlEqualityComparer = new UriComparer();

    readonly Uri _originUrl;
    readonly ISecretsStore _secretsStore;
    readonly KnownCloudGitProvider _knownGitProvider;
    readonly bool _isPublic;

    string? _repoName;
    string? _prefixPAT;
    string? _readPATKeyName;
    UsernamePasswordCredentials? _readCreds;
    string? _writePATKeyName;
    UsernamePasswordCredentials? _writeCreds;
    IGitRepositoryAccessKey? _revertedAccessKey;

    /// <summary>
    /// Initializes a new <see cref="GitRepositoryKey"/>.
    /// This calls <see cref="ThrowArgumentExceptionOnInvalidUrl"/> on the <paramref name="url"/>. Use the
    /// <see cref="Create(IActivityMonitor, ISecretsStore, Uri, bool)"/> factory method for exception-less
    /// initialization.
    /// </summary>
    /// <param name="secretsStore">The secrets store to use.</param>
    /// <param name="url">The url of the remote.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    public GitRepositoryKey( ISecretsStore secretsStore, Uri url, bool isPublic )
        : this( url, isPublic, secretsStore )
    {
        ThrowArgumentExceptionOnInvalidUrl( url );
    }

    GitRepositoryKey( Uri url, bool isPublic, ISecretsStore secretsStore )
    {
        _originUrl = url;
        _secretsStore = secretsStore;
        _isPublic = isPublic;

        if( url.Scheme == Uri.UriSchemeFile ) _knownGitProvider = KnownCloudGitProvider.FileSystem;
        else if( url.Authority.Equals( "github.com", StringComparison.Ordinal ) ) _knownGitProvider = KnownCloudGitProvider.GitHub;
        else if( url.Authority.Equals( "gitlab.com", StringComparison.Ordinal ) ) _knownGitProvider = KnownCloudGitProvider.GitLab;
        else if( url.Authority.Equals( "dev.azure.com", StringComparison.Ordinal ) ) _knownGitProvider = KnownCloudGitProvider.AzureDevOps;
        else if( url.Authority.Equals( "bitbucket.org", StringComparison.Ordinal ) ) _knownGitProvider = KnownCloudGitProvider.Bitbucket;
    }

    /// <summary>
    /// Creates a <see cref="GitRepositoryKey"/> only if <paramref name="url"/> is valid or emits an error and returns null.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store to use.</param>
    /// <param name="url">The url of the remote.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    /// <returns>The repository key on success, null on error.</returns>
    public static GitRepositoryKey? Create( IActivityMonitor monitor, ISecretsStore secretsStore, Uri url, bool isPublic )
    {
        var urlError = GetRepositoryUrlError( url );
        if( urlError != null )
        {
            monitor.Error( urlError );
            return null;
        }
        return new GitRepositoryKey( url, isPublic, secretsStore );
    }

    /// <summary>
    /// Gets whether the Git repository is public.
    /// </summary>
    public bool IsPublic => _isPublic;

    /// <summary>
    /// Gets the remote origin url.
    /// <para>
    /// See <see cref="GetRepositoryUrlError(Uri)"/> for invariants.
    /// </para>
    /// </summary>
    public Uri OriginUrl => _originUrl;

    /// <summary>
    /// Gets the valid repository name.
    /// </summary>
    [MemberNotNull( nameof( _repoName ) )]
    public string RepositoryName => _repoName ??= new string( System.IO.Path.GetFileName( _originUrl.ToString().AsSpan() ) );

    /// <summary>
    /// Gets whether this <see cref="RepositoryName"/> ends with "-Stack" (case insensitive) and the prefix, the stack name,
    /// is at least 2 characters long.
    /// </summary>
    public bool IsStackRepository => RepositoryName.EndsWith( "-Stack", StringComparison.OrdinalIgnoreCase ) && _repoName.Length >= 8;

    /// <inheritdoc />
    public KnownCloudGitProvider KnownGitProvider => _knownGitProvider;

    /// <inheritdoc />
    public bool GetReadCredentials( IActivityMonitor monitor, out UsernamePasswordCredentials? creds )
    {
        return DoReadCredentials( monitor, _isPublic, out creds );
    }

    bool DoReadCredentials( IActivityMonitor monitor, bool isPublic, out UsernamePasswordCredentials? creds )
    {
        if( isPublic )
        {
            creds = null;
            return true;
        }
        if( _readCreds == null )
        {
            var pat = _secretsStore.TryGetRequiredSecret( monitor, WritePATKeyName, ReadPATKeyName );
            _readCreds = pat != null
                            ? new UsernamePasswordCredentials() { Username = "CKli", Password = pat }
                            : null;
        }
        creds = _readCreds;
        return creds != null;
    }

    /// <inheritdoc />
    public bool GetWriteCredentials( IActivityMonitor monitor, [NotNullWhen( true )] out UsernamePasswordCredentials? creds )
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

    /// <inheritdoc />
    public string ReadPATKeyName => _readPATKeyName ??= PrefixPAT + "_READ_PAT";

    /// <inheritdoc />
    public string WritePATKeyName => _writePATKeyName ??= PrefixPAT + "_WRITE_PAT";

    /// <inheritdoc />
    public IGitRepositoryAccessKey ToPublicAccessKey() => _isPublic ? this : GetRevertedAccessKey();

    /// <inheritdoc />
    public IGitRepositoryAccessKey ToPrivateAccessKey() => _isPublic ? GetRevertedAccessKey() : this;

    IGitRepositoryAccessKey GetRevertedAccessKey() => _revertedAccessKey ??= new RevertedKey( this );

    /// <summary>
    /// Common PAT prefix built from <see cref="KnownGitProvider"/> and <see cref="OriginUrl"/>.
    /// </summary>
    public string PrefixPAT
    {
        get
        {
            if( _prefixPAT == null )
            {
                Throw.DebugAssert( "Absolute https:// or file:// without query part.",
                                      (_originUrl.Scheme.Equals( "https", StringComparison.Ordinal )
                                        || _originUrl.Scheme.Equals( "file", StringComparison.Ordinal ))
                                      && _originUrl.IsAbsoluteUri
                                      && _originUrl.Query.Length == 0 );

                switch( KnownGitProvider )
                {
                    case KnownCloudGitProvider.Unknown:
                        string prefix = _originUrl.GetComponents( UriComponents.Host | UriComponents.Port | UriComponents.KeepDelimiter,
                                                                  UriFormat.Unescaped );
                        prefix = Secure( prefix );
                        if( prefix.Length == 0 )
                        {
                            Throw.CKException( $"Unable to derive a PAT prefix from url '{_originUrl}'." );
                        }
                        _prefixPAT = ConcatFirstPathPart( prefix, _originUrl );
                        break;
                    case KnownCloudGitProvider.AzureDevOps:
                        {
                            _prefixPAT = ConcatFirstPathPart( "AZUREDEVOPS_", OriginUrl );
                            break;
                        }
                    case KnownCloudGitProvider.GitHub:
                        {
                            _prefixPAT = ConcatFirstPathPart( "GITHUB_", OriginUrl );
                            break;
                        }
                    case KnownCloudGitProvider.GitLab:
                        {
                            _prefixPAT = ConcatFirstPathPart( "GITLAB_", OriginUrl );
                            break;
                        }
                    case KnownCloudGitProvider.Bitbucket:
                        {
                            _prefixPAT = ConcatFirstPathPart( "BITBUCKET_", OriginUrl );
                            break;
                        }
                    case KnownCloudGitProvider.FileSystem:
                        {
                            _prefixPAT = FileSystemPrefixPAT;
                            break;
                        }
                    default:
                        Throw.NotSupportedException();
                        break;
                }
            }
            return _prefixPAT;

            static string Secure( string s )
            {
                Throw.DebugAssert( "Already in upper case.", s.ToUpperInvariant() == s );
                BadPATChars().Replace( s, "_" );
                return s;
            }

            static string ConcatFirstPathPart( string prefix, Uri originUrl )
            {
                Throw.DebugAssert( "Already in upper case.", prefix.ToUpperInvariant() == prefix );
                var part = GetFirstPathPart( originUrl );
                return part.Length > 0
                        ? part[0] != '_' && prefix[^1] != '_'
                                        ? prefix + '_' + part
                                        : prefix + part
                        : prefix;
            }

            static string GetFirstPathPart( Uri originUrl )
            {
                var m = FirstPathPart().Match( originUrl.AbsolutePath );
                return m.Success ? Secure( m.Groups[1].Value.ToUpperInvariant() ) : "";
            }
        }
    }

    [GeneratedRegex( "[^A-Z_0-9]" )]
    private static partial Regex BadPATChars();

    [GeneratedRegex( @"^/*([^\/]*)", RegexOptions.CultureInvariant )]
    private static partial Regex FirstPathPart();

    /// <summary>
    /// Overridden to return the OriginUrl. 
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => _originUrl.ToString();

    /// <summary>
    /// Validates a Url that can be used for a GitRepositoryKey.
    /// <list type="bullet">
    ///     <item>The url is absolute (<see cref="Uri.IsAbsoluteUri"/> is true).</item>
    ///     <item>The url has no query part (<see cref="Uri.Query"/> is empty).</item>
    ///     <item>The url doesn't end with ".git". See https://stackoverflow.com/a/11069413/.</item>
    ///     <item>The only allowed schemes are <see cref="Uri.UriSchemeFile"/> and <see cref="Uri.UriSchemeHttps"/>.</item>
    ///     <item>For "https://", the <see cref="Uri.Authority"/> is in lowercase.</item>
    ///     <item>The url must end with a segment that satisfies <see cref="WorldName.IsValidRepositoryName"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="url">The url to validate.</param>
    /// <returns>A non null message if something's wrong.</returns>
    public static string? GetRepositoryUrlError( Uri url )
    {
        if( !url.IsAbsoluteUri || url.Query.Length > 0 )
        {
            return $"Invalid Url: '{url}' must be absolute and have no query part.";
        }
        if( url.Scheme != Uri.UriSchemeFile )
        {
            var a = url.Authority;
            if( a.ToLowerInvariant() != a )
            {
                return $"Invalid Url: '{url}' must have lower case authority ('{a}' should be '{a.ToLowerInvariant()}').";
            }
            if( url.Scheme != Uri.UriSchemeHttps )
            {
                return $"Invalid Url: '{url}' must have '{Uri.UriSchemeHttps}' or '{Uri.UriSchemeFile}' scheme.";
            }
        }
        var sUrl = url.ToString();
        if( sUrl.EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) )
        {
            return $"Invalid Url: '{sUrl}' must not end with '.git'.";
        }
        var n = System.IO.Path.GetFileName( sUrl.AsSpan() );
        if( !WorldName.IsValidRepositoryName( n ) )
        {
            return $"Invalid Url: the last path part must be a valid repository name: '{url}'.";
        }
        return null;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if <see cref="GetRepositoryUrlError(Uri)"/> returns
    /// a non null message.
    /// </summary>
    /// <param name="url">The url to validate (null throws a <see cref="ArgumentNullException"/>).</param>
    public static void ThrowArgumentExceptionOnInvalidUrl( [NotNull] Uri? url, string parameterName = "url" )
    {
        Throw.CheckNotNullArgument( url, parameterName );
        var error = GetRepositoryUrlError( url );
        if( error != null )
        {
            Throw.ArgumentException( nameof( url ), error );
        }
    }
}
