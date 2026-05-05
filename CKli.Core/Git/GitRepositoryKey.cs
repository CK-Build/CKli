using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

/// <summary>
/// Centralizes url manipulation and provides the <see cref="AccessKey"/> to the repository.
/// </summary>
public partial class GitRepositoryKey
{
    sealed class UriComparer : IEqualityComparer<Uri>
    {
        public bool Equals( Uri? x, Uri? y ) => StringComparer.OrdinalIgnoreCase.Equals( x?.ToString(), y?.ToString() );

        public int GetHashCode( [DisallowNull] Uri obj ) => StringComparer.OrdinalIgnoreCase.GetHashCode( obj.ToString() );
    }

    /// <summary>
    /// The <see cref="IGitRepositoryAccessKey.PrefixPAT"/> used for file system provider.
    /// </summary>
    public const string FileSystemPrefixPAT = "FILESYSTEM_GIT";

    /// <summary>
    /// The fake remote url used by bare repositories.
    /// </summary>
    public const string BareRepositoryFakeUrl = "file://Bare-Fake-Remote/";

    /// <summary>
    /// The fake remote url used by orphan repositories.
    /// </summary>
    public const string OrphanRepositoryFakeUrl = "file://Orphan-Fake-Remote/";

    /// <summary>
    /// An equality comparer for Url that uses <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public static readonly IEqualityComparer<Uri> OrdinalIgnoreCaseUrlEqualityComparer = new UriComparer();

    readonly Uri _originUrl;
    readonly ISecretsStore _secretsStore;
    IGitRepositoryAccessKey? _accessKey;
    string? _repoName;
    bool _isPublic;

    static Uri? _bareFakeUrl;
    static Uri? _orphanFakeUrl;

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
        _isPublic = isPublic;
        _secretsStore = secretsStore;
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
    /// Creates a <see cref="GitRepositoryKey"/> for a bare repository without remote origin: <see cref="OriginUrl"/> is <see cref="BareRepositoryFakeUrl"/>.
    /// </summary>
    /// <param name="secretsStore">The secrets store to use.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    /// <returns>The repository key on success, null on error.</returns>
    public static GitRepositoryKey CreateBareRepositoryKey( ISecretsStore secretsStore, bool isPublic )
    {
        _bareFakeUrl ??= new Uri( BareRepositoryFakeUrl );
        return new GitRepositoryKey( _bareFakeUrl, isPublic, secretsStore );
    }

    /// <summary>
    /// Creates a <see cref="GitRepositoryKey"/> for an orphan repository that has no remote origin: <see cref="OriginUrl"/> is <see cref="OrphanRepositoryFakeUrl"/>.
    /// </summary>
    /// <param name="secretsStore">The secrets store to use.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    /// <returns>The repository key on success, null on error.</returns>
    public static GitRepositoryKey CreateOrphanRepositoryKey( ISecretsStore secretsStore, bool isPublic )
    {
        _orphanFakeUrl ??= new Uri( OrphanRepositoryFakeUrl );
        return new GitRepositoryKey( _orphanFakeUrl, isPublic, secretsStore );
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
    /// Gets the access key for this repository.
    /// </summary>
    public IGitRepositoryAccessKey AccessKey => _accessKey ??= IsBareRepository || IsOrphanRepository
                                                                ? GitRepositoryAccessKey.GetFileSystemAccessKey( _secretsStore )
                                                                : GitRepositoryAccessKey.Get( _secretsStore, _originUrl, _isPublic );

    /// <summary>
    /// Attempts to retrieve the Git hosting provider and normalized repository path for this <see cref="OriginUrl"/>.
    /// <para>
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="hostingProvider">Outputs the Git hosting provider on success.</param>
    /// <param name="repoPath">Outputs the normalized repository path on success.</param>
    /// <returns>true if the hosting provider and repository path were successfully retrieved; otherwise, false.</returns>
    public bool TryGetHostingInfo( IActivityMonitor monitor, [NotNullWhen(true)]out GitHostingProvider? hostingProvider, out NormalizedPath repoPath )
    {
        hostingProvider = AccessKey.HostingProvider;
        if( hostingProvider == null )
        {
            monitor.Error( $"Could not determine hosting provider for url: '{_originUrl}'." );
            repoPath = default;
            return false;
        }
        repoPath = hostingProvider.GetRepositoryPathFromUrl( monitor, this );
        return !repoPath.IsEmptyPath;
    }

    /// <summary>
    /// Gets the valid repository name.
    /// </summary>
    [MemberNotNull( nameof( _repoName ) )]
    public string RepositoryName => _repoName ??= new string( System.IO.Path.GetFileName( _originUrl.ToString().AsSpan() ) );

    /// <summary>
    /// Gets whether this key is for a bare repository: the <see cref="OriginUrl"/> is <see cref="BareRepositoryFakeUrl"/>.
    /// </summary>
    public bool IsBareRepository => ReferenceEquals( _originUrl.OriginalString, BareRepositoryFakeUrl );

    /// <summary>
    /// Gets whether this key is for an orphan repository: the <see cref="OriginUrl"/> is <see cref="OrphanRepositoryFakeUrl"/>.
    /// </summary>
    public bool IsOrphanRepository => ReferenceEquals( _originUrl.OriginalString, OrphanRepositoryFakeUrl );

    /// <summary>
    /// Gets whether this <see cref="RepositoryName"/> ends with "-Stack" (case insensitive) and the prefix, the stack name,
    /// is at least 2 characters long.
    /// </summary>
    public bool IsStackRepository => RepositoryName.EndsWith( "-Stack", StringComparison.OrdinalIgnoreCase ) && _repoName.Length >= 8;

    /// <summary>
    /// Checks that <see cref="OriginUrl"/> is a "-Stack" url or emits an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="stackNameFromUrl">The stack name is the prefix of the <see cref="RepositoryName"/>.</param>
    /// <returns>True if this is a Stack repository, false otherwise.</returns>
    public bool CheckOriginUrlStackSuffix( IActivityMonitor monitor, [NotNullWhen( true )] out string? stackNameFromUrl )
    {
        if( IsStackRepository )
        {
            stackNameFromUrl = RepositoryName[..^6];
            return true;
        }
        stackNameFromUrl = null;
        monitor.Error( $"The repository Url '{OriginUrl}' must have '-Stack' suffix (and the stack name must be at least 2 characters)." );
        return false;
    }

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
    /// <param name="parameterName">The name of the parameter used for the ArgumentException.</param>
    public static void ThrowArgumentExceptionOnInvalidUrl( [NotNull] Uri? url, string parameterName = "url" )
    {
        Throw.CheckNotNullArgument( url, parameterName );
        var error = GetRepositoryUrlError( url );
        if( error != null )
        {
            Throw.ArgumentException( nameof( url ), error );
        }
    }

    /// <summary>
    /// Extension point (factory method for <see cref="IGitRepositoryAccessKey"/>) to be used by external "Core plugins"
    /// in order to support specific url formats and hosting providers.
    /// <para>
    /// If the <paramref name="hostingProviderFactory"/> is null, the created access key will have no hosting provider. 
    /// </para>
    /// </summary>
    /// <param name="prefixPAT">The <see cref="IGitRepositoryAccessKey.PrefixPAT"/> that identifies the key.</param>
    /// <param name="secretsStore">The secret store.</param>
    /// <param name="isPublic">See <see cref="IGitRepositoryAccessKey.IsPublic"/>.</param>
    /// <param name="hostingProviderFactory">
    /// Optional function that can associate the <see cref="IGitRepositoryAccessKey.HostingProvider"/> to the
    /// newly created key. When not null, this is called immediately.
    /// </param>
    /// <returns>The repository key.</returns>
    public static IGitRepositoryAccessKey CreateAccessKey( string prefixPAT,
                                                           ISecretsStore secretsStore,
                                                           bool? isPublic,
                                                           Func<IGitRepositoryAccessKey, GitHostingProvider?>? hostingProviderFactory )
    {
        return new GitRepositoryAccessKey( prefixPAT, secretsStore, isPublic, hostingProviderFactory );
    }

}
