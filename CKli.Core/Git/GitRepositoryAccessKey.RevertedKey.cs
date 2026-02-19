using CK.Core;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

partial class GitRepositoryAccessKey
{
    sealed class RevertedKey : IGitRepositoryAccessKey
    {
        readonly GitRepositoryAccessKey _origin;
        string? _toString;

        public RevertedKey( GitRepositoryAccessKey origin )
        {
            Throw.DebugAssert( "Never instantiated for null IsPublic.", origin.IsPublic is not null );
            _origin = origin;
        }

        public bool? IsPublic => !_origin.IsPublic!.Value;

        public string PrefixPAT => _origin.PrefixPAT;

        public string ReadPATKeyName => _origin.ReadPATKeyName;

        public string WritePATKeyName => _origin.WritePATKeyName;

        public GitHostingProvider? HostingProvider => _origin.HostingProvider;

        public bool GetReadCredentials( IActivityMonitor monitor, out UsernamePasswordCredentials? creds )
        {
            return _origin.DoReadCredentials( monitor, !_origin.IsPublic, out creds );
        }

        public bool GetWriteCredentials( IActivityMonitor monitor, [NotNullWhen( true )] out UsernamePasswordCredentials? creds )
        {
            return _origin.GetWriteCredentials( monitor, out creds );
        }

        public IGitRepositoryAccessKey ToPrivateAccessKey() => !_origin.IsPublic!.Value ? this : _origin;

        public IGitRepositoryAccessKey ToPublicAccessKey() => _origin.IsPublic!.Value ? _origin : this;

        public override string ToString() => _toString ??= AccessKeyToString( PrefixPAT, !_origin.IsPublic!.Value );
    }

}
