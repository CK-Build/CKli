using CK.Core;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

public partial class GitRepositoryKey
{
    sealed class RevertedKey : IGitRepositoryAccessKey
    {
        readonly OriginalKey _origin;
        string? _toString;

        public RevertedKey( OriginalKey origin ) => _origin = origin;

        public bool IsPublic => !_origin.IsPublic;

        public KnownCloudGitProvider KnownGitProvider => _origin.KnownGitProvider;

        public string PrefixPAT => _origin.PrefixPAT;

        public string ReadPATKeyName => _origin.ReadPATKeyName;

        public string WritePATKeyName => _origin.WritePATKeyName;

        public bool GetReadCredentials( IActivityMonitor monitor, out UsernamePasswordCredentials? creds )
        {
            return _origin.DoReadCredentials( monitor, !_origin.IsPublic, out creds );
        }

        public bool GetWriteCredentials( IActivityMonitor monitor, [NotNullWhen( true )] out UsernamePasswordCredentials? creds )
        {
            return _origin.GetWriteCredentials( monitor, out creds );
        }

        public IGitRepositoryAccessKey ToPrivateAccessKey() => _origin.IsPublic ? this : _origin;

        public IGitRepositoryAccessKey ToPublicAccessKey() => _origin.IsPublic ? _origin : this;

        public override string ToString() => _toString ??= $"{PrefixPAT} ({(_origin.IsPublic ? "private" : "public")})";
    }
}
