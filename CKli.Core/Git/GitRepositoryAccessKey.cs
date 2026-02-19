using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace CKli.Core;

/// <summary>
/// Internal implementation of the primary <see cref="IGitRepositoryAccessKey"/>.
/// These access keys are shared (indexed by PrefixPAT): <see cref="Get(ISecretsStore, Uri, bool)"/>
/// finds or creates them. 
/// <para>
/// Externally, this can only be obtained through the <see cref="GitRepositoryKey.AccessKey"/> and to support
/// externally resolved access keys, the static <see cref="GitRepositoryKey.CreateAccessKey"/> method can be used.
/// </para>
/// </summary>
sealed partial class GitRepositoryAccessKey : IGitRepositoryAccessKey
{
    readonly ISecretsStore _secretsStore;
    readonly string _prefixPAT;
    readonly bool? _isPublic;
    readonly GitHostingProvider? _hostingProvider;
    string? _readPATKeyName;
    UsernamePasswordCredentials? _readCreds;
    string? _writePATKeyName;
    UsernamePasswordCredentials? _writeCreds;
    IGitRepositoryAccessKey? _revertedAccessKey;
    string? _toString;

    static readonly Dictionary<string, GitRepositoryAccessKey> _accessKeys = [];

    internal static IGitRepositoryAccessKey Get( ISecretsStore secretsStore, Uri url, bool isPublic )
    {
        Throw.DebugAssert( GitRepositoryKey.GetRepositoryUrlError( url ) == null );
        var p = FindOrCreate( secretsStore, url, firstIsPublic: isPublic );
        return isPublic ? p.ToPublicAccessKey() : p.ToPrivateAccessKey();
    }

    static GitRepositoryAccessKey FindOrCreate( ISecretsStore secretsStore, Uri url, bool firstIsPublic )
    {
        // Exit early for file://.
        if( url.Scheme == Uri.UriSchemeFile )
        {
            if( !_accessKeys.TryGetValue( GitRepositoryKey.FileSystemPrefixPAT, out var exists ) )
            {
                exists = new GitRepositoryAccessKey( GitRepositoryKey.FileSystemPrefixPAT,
                                                     secretsStore,
                                                     isPublic: null,
                                                     key => new GitHosting.Providers.FileSystemProvider( key ) );
                _accessKeys.Add( GitRepositoryKey.FileSystemPrefixPAT, exists );
            }
            return exists;
        }
        // General https:// case.
        //
        // url.GetLeftPart( UriPartial.Authority ) returns:
        // UriComponents.Scheme | UriComponents.UserInfo | UriComponents.Host | UriComponents.Port in the escaped format.
        //
        // It is a good thing to keep the UriComponents.UserInfo for the base url.
        //
        // However, the .Net Uri.Authority returns only UriComponents.Host | UriComponents.Port (that is wrong regarding
        // the RFC but this is legacy from .Net Framework).
        // We currently follow the RFC and keep the UserInfo in our authority (but this may change).
        //
        var baseUrl = url.GetLeftPart( UriPartial.Authority );
        Throw.DebugAssert( """
                               The host part is normalized by Uri to be in lower case (https://datatracker.ietf.org/doc/html/rfc9110#section-4.2.3).
                               "The scheme and host are case-insensitive and normally provided in lowercase;"
                               """,
                   baseUrl.ToLowerInvariant() == baseUrl );
        Throw.DebugAssert( baseUrl.StartsWith( "https://" ) && "https://".Length == 8 );
        var authority = baseUrl[8..];

        // All the known cloud providers happens to have the "owner" or the "organization" or the "WorkspaceId"
        // in the first part of the url's path.
        // We use this common design here and we also use it for any unknown provider (below).
        if( authority.Equals( "github.com", StringComparison.Ordinal ) )
        {
            var prefix = ConcatFirstPathPart( "GITHUB_", url );
            if( !_accessKeys.TryGetValue( prefix, out var exists ) )
            {
                // We consider for https://github.com that IsDefaultPublic is determined by the
                // first repository resolution. This "works" for us because a Stack is either public or private and
                // all repositories in it are public or private, including the Stack repository.
                // If we were to "really" handle this, we should add the 'isPublic' to the key and initializes
                // 2 hosting repositories instead of 1: public access keys will be bound to the IsDefaultPublic = true
                // instance (resp. private). (And we should also enhance this: currently there is no
                // "I am a private only host").
                exists = new GitRepositoryAccessKey( prefix,
                                                     secretsStore,
                                                     isPublic: firstIsPublic,
                                                     key => new GitHosting.Providers.GitHubProvider( key ) );
                _accessKeys.Add( prefix, exists );
            }
            return exists;
        }
        if( authority.Equals( "gitlab.com", StringComparison.Ordinal ) )
        {
            var prefix = ConcatFirstPathPart( "GITLAB_", url );
            if( !_accessKeys.TryGetValue( prefix, out var exists ) )
            {
                // Same as GitHub.
                exists = new GitRepositoryAccessKey( prefix,
                                                     secretsStore,
                                                     isPublic: firstIsPublic,
                                                     key => new GitHosting.Providers.GitLabProvider( key ) );
                _accessKeys.Add( prefix, exists );
            }
            return exists;
        }
        if( authority.Equals( "dev.azure.com", StringComparison.Ordinal ) )
        {
            var prefix = ConcatFirstPathPart( "AZUREDEVOPS_", url );
            if( !_accessKeys.TryGetValue( prefix, out var exists ) )
            {
                // IsDefaultPublic is always false.
                exists = new GitRepositoryAccessKey( prefix,
                                                     secretsStore,
                                                     isPublic: false,
                                                     hostingProviderFactory: null );
                _accessKeys.Add( prefix, exists );
            }
            return exists;
        }

        if( authority.Equals( "bitbucket.org", StringComparison.Ordinal ) )
        {
            var prefix = ConcatFirstPathPart( "BITBUCKET_", url );
            if( !_accessKeys.TryGetValue( prefix, out var exists ) )
            {
                // IsDefaultPublic is always false.
                exists = new GitRepositoryAccessKey( prefix,
                                                     secretsStore,
                                                     isPublic: false,
                                                     hostingProviderFactory: null );
                _accessKeys.Add( prefix, exists );
            }
            return exists;
        }
        // Not a known cloud provider.
        //
        // If we introduce "Core Plugins" one day, the (baseUrl, authority, url) should
        // be submitted to them WITH the existing _accessKeys and they may be able to find or create
        // a key (with its hosting provider or not if it's only to handle special urls...).
        //
        // This should look like:
        //
        // var externallyResolved = CorePlugins.FindOrCreateRepositoryAccessKey( secretsStore, _accessKeys, baseUrl, authority, url );
        // if( externallyResolved != null )
        // {
        //    return externallyResolved;
        // }

        // Before falling back to unknown hosting, try to handle known providers through url naming conventions.
        if( authority.Contains( "github", StringComparison.Ordinal ) )
        {
            var prefix = Secure( authority.ToUpperInvariant() );
            if( !_accessKeys.TryGetValue( prefix, out var exists ) )
            {
                exists = new GitRepositoryAccessKey( prefix,
                                                     secretsStore,
                                                     isPublic: firstIsPublic,
                                                     key => new GitHosting.Providers.GitHubProvider( baseUrl, key, authority ) );
                _accessKeys.Add( prefix, exists );
            }
            return exists;
        }
        if( authority.Contains( "gitlab", StringComparison.Ordinal ) )
        {
            var prefix = Secure( authority.ToUpperInvariant() );
            if( !_accessKeys.TryGetValue( prefix, out var exists ) )
            {
                exists = new GitRepositoryAccessKey( prefix,
                                                     secretsStore,
                                                     isPublic: firstIsPublic,
                                                     key => new GitHosting.Providers.GitLabProvider( baseUrl, key, authority ) );
                _accessKeys.Add( prefix, exists );
            }
            return exists;
        }
        if( authority.Contains( "gitea", StringComparison.Ordinal ) )
        {
            var prefix = Secure( authority.ToUpperInvariant() );
            if( !_accessKeys.TryGetValue( prefix, out var exists ) )
            {
                exists = new GitRepositoryAccessKey( prefix,
                                                     secretsStore,
                                                     isPublic: firstIsPublic,
                                                     key => new GitHosting.Providers.GiteaProvider( baseUrl, key, authority ) );
                _accessKeys.Add( prefix, exists );
            }
            return exists;
        }

        // Fallback: we have a PAT (unless url is REALLY buggy) but obviously no hosting provider.
        {
            var prefix = Secure( authority.ToUpperInvariant() );
            if( prefix.Length == 0 )
            {
                Throw.CKException( $"Unable to derive a PAT prefix from url '{url}'." );
            }
            if( !_accessKeys.TryGetValue( prefix, out var exists ) )
            {
                exists = new GitRepositoryAccessKey( prefix,
                                                     secretsStore,
                                                     null,
                                                     hostingProviderFactory: null );
                _accessKeys.Add( prefix, exists );
            }
            return exists;
        }


        static string Secure( string s )
        {
            Throw.DebugAssert( "Already in upper case.", s.ToUpperInvariant() == s );
            return BadPATChars().Replace( s, "_" );
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


    internal GitRepositoryAccessKey( string prefixPAT,
                                     ISecretsStore secretsStore,
                                     bool? isPublic,
                                     Func<GitRepositoryAccessKey,GitHostingProvider?>? hostingProviderFactory )
    {
        _prefixPAT = prefixPAT;
        _secretsStore = secretsStore;
        _isPublic = isPublic;
        _hostingProvider = hostingProviderFactory?.Invoke( this );
    }

    public bool? IsPublic => _isPublic;

    public string PrefixPAT => _prefixPAT;

    public string ReadPATKeyName => _readPATKeyName ??= _isPublic is null ? _prefixPAT : _prefixPAT + "_READ_PAT";

    public string WritePATKeyName => _writePATKeyName ??= _isPublic is null ? _prefixPAT : _prefixPAT + "_WRITE_PAT";

    public GitHostingProvider? HostingProvider => _hostingProvider;

    public bool GetReadCredentials( IActivityMonitor monitor, out UsernamePasswordCredentials? creds )
    {
        return DoReadCredentials( monitor, _isPublic, out creds );
    }

    bool DoReadCredentials( IActivityMonitor monitor, bool? isPublic, out UsernamePasswordCredentials? creds )
    {
        if( isPublic is null or true )
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

    public IGitRepositoryAccessKey ToPublicAccessKey() => _isPublic is null or true ? this : GetRevertedAccessKey();

    public IGitRepositoryAccessKey ToPrivateAccessKey() => _isPublic is null or false ? this : GetRevertedAccessKey();

    IGitRepositoryAccessKey GetRevertedAccessKey() => _revertedAccessKey ??= new RevertedKey( this );

    static string AccessKeyToString( string prefix, bool isPublic )
    {
        return prefix + isPublic switch { true => " (public)", false => " (private)" };
    }

    public override string ToString() => _toString ??= AccessKeyToString( PrefixPAT, _isPublic ?? true );

    [GeneratedRegex( "[^A-Z_0-9]", RegexOptions.CultureInvariant )]
    private static partial Regex BadPATChars();

    [GeneratedRegex( @"^/*([^\/]*)", RegexOptions.CultureInvariant )]
    private static partial Regex FirstPathPart();

}
