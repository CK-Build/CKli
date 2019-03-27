using System;
using System.Diagnostics;
using CSemVer;
using SimpleGitVersion;

namespace CK.Env
{
    class CommitAssemblyBuildInfoFromRepo : ICommitAssemblyBuildInfo
    {
        readonly RepositoryInfo _info;

        public CommitAssemblyBuildInfoFromRepo( RepositoryInfo info )
        {
            Debug.Assert( info != null );
            _info = info;
            BuildConfiguration = info.FinalNuGetVersion.Prerelease.Length == 0 || info.FinalNuGetVersion.Prerelease == "rc"
                                   ? "Release"
                                   : "Debug";
            AssemblyVersion = $"{info.FinalNuGetVersion.Major}.{info.FinalNuGetVersion.Minor}";
            if( info.CIRelease != null )
            {
                FileVersion = info.CIRelease.BaseTag.ToStringFileVersion( true );
            }
            else if( info.ValidReleaseTag != null )
            {
                FileVersion = info.ValidReleaseTag.ToStringFileVersion( false );
            }
            else FileVersion = CSemVer.InformationalVersion.ZeroFileVersion;
        }

        public string BuildConfiguration { get; }

        public string CommitSha => _info.CommitSha ?? CSemVer.InformationalVersion.ZeroCommitSha;

        public DateTime CommitDateUtc => _info.CommitDateUtc;

        public string InformationalVersion => _info.FinalInformationalVersion;

        public SVersion SemVersion => _info.FinalSemVersion;

        public SVersion NuGetVersion => _info.FinalNuGetVersion;

        public string AssemblyVersion { get; }

        public string FileVersion { get; }
}
}
