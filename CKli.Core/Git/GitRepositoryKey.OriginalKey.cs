using CK.Core;
using LibGit2Sharp;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace CKli.Core;

public partial class GitRepositoryKey
{
    sealed partial class OriginalKey : IGitRepositoryAccessKey
    {
        internal readonly Uri _originUrl;
        readonly ISecretsStore _secretsStore;
        readonly KnownCloudGitProvider _knownGitProvider;
        readonly bool _isPublic;
        string? _prefixPAT;
        string? _readPATKeyName;
        UsernamePasswordCredentials? _readCreds;
        string? _writePATKeyName;
        UsernamePasswordCredentials? _writeCreds;
        IGitRepositoryAccessKey? _revertedAccessKey;
        string? _toString;

        public OriginalKey( Uri originUrl, ISecretsStore secretsStore, KnownCloudGitProvider knownGitProvider, bool isPublic )
        {
            _originUrl = originUrl;
            _secretsStore = secretsStore;
            _knownGitProvider = knownGitProvider;
            _isPublic = isPublic;
        }

        public bool IsPublic => _isPublic;

        public KnownCloudGitProvider KnownGitProvider => _knownGitProvider;

        public bool GetReadCredentials( IActivityMonitor monitor, out UsernamePasswordCredentials? creds )
        {
            return DoReadCredentials( monitor, _isPublic, out creds );
        }

        internal bool DoReadCredentials( IActivityMonitor monitor, bool isPublic, out UsernamePasswordCredentials? creds )
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

        public string ReadPATKeyName => _readPATKeyName ??= PrefixPAT + "_READ_PAT";

        public string WritePATKeyName => _writePATKeyName ??= PrefixPAT + "_WRITE_PAT";

        public IGitRepositoryAccessKey ToPublicAccessKey() => _isPublic ? this : GetRevertedAccessKey();

        public IGitRepositoryAccessKey ToPrivateAccessKey() => _isPublic ? GetRevertedAccessKey() : this;

        IGitRepositoryAccessKey GetRevertedAccessKey() => _revertedAccessKey ??= new RevertedKey( this );

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
                                _prefixPAT = ConcatFirstPathPart( "AZUREDEVOPS_", _originUrl );
                                break;
                            }
                        case KnownCloudGitProvider.GitHub:
                            {
                                _prefixPAT = ConcatFirstPathPart( "GITHUB_", _originUrl );
                                break;
                            }
                        case KnownCloudGitProvider.GitLab:
                            {
                                _prefixPAT = ConcatFirstPathPart( "GITLAB_", _originUrl );
                                break;
                            }
                        case KnownCloudGitProvider.Bitbucket:
                            {
                                _prefixPAT = ConcatFirstPathPart( "BITBUCKET_", _originUrl );
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

        public override string ToString() => _toString ??= $"{PrefixPAT} ({(_isPublic ? "public" : "private")})";

        [GeneratedRegex( "[^A-Z_0-9]" )]
        private static partial Regex BadPATChars();

        [GeneratedRegex( @"^/*([^\/]*)", RegexOptions.CultureInvariant )]
        private static partial Regex FirstPathPart();
    }
}
