using CSemVer;
using SimpleGitVersion;
using System;
using System.Diagnostics;

namespace CK.Env
{
    class CommitAssemblyBuildInfoFromRepo : ICommitBuildInfo
    {
        readonly RepositoryInfo _info;

        public CommitAssemblyBuildInfoFromRepo( RepositoryInfo info )
        {
            Debug.Assert( info != null );
            _info = info;
            BuildConfiguration = info.FinalVersion.Prerelease.Length == 0 || info.FinalVersion.AsCSVersion?.PrereleaseName == "rc"
                                   ? "Release"
                                   : "Debug";
            AssemblyVersion = $"{info.FinalVersion.Major}.{info.FinalVersion.Minor}";
            if( info.CIRelease != null )
            {
                if( info.CIRelease.IsZeroTimed )
                {
                    FileVersion = CSemVer.InformationalVersion.ZeroFileVersion;
                }
                else
                {
                    Debug.Assert( info.CIRelease.BaseTag.AsCSVersion != null, "In LastReleaseBased mode, there is a valid CSVersion base tag." );
                    FileVersion = info.CIRelease.BaseTag.AsCSVersion.ToStringFileVersion( true );
                }
            }
            else if( info.ValidReleaseTag != null )
            {
                FileVersion = info.ValidReleaseTag.ToStringFileVersion( false );
            }
            else
            {
                FileVersion = CSemVer.InformationalVersion.ZeroFileVersion;
            }
        }

        public string BuildConfiguration { get; }

        public string CommitSha => _info.CommitSha ?? CSemVer.InformationalVersion.ZeroCommitSha;

        public DateTime CommitDateUtc => _info.CommitDateUtc;

        public string InformationalVersion => _info.FinalInformationalVersion;

        public SVersion Version => _info.FinalVersion;

        public string AssemblyVersion { get; }

        public string FileVersion { get; }
    }
}
