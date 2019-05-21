using CSemVer;
using System;

namespace CK.Env
{
    /// <summary>
    /// Implements <see cref="ICommitAssemblyBuildInfo"/>.
    /// </summary>
    public class CommitAssemblyBuildInfo : ICommitAssemblyBuildInfo
    {
        class ZeroBuild : ICommitAssemblyBuildInfo
        {
            public string BuildConfiguration => "Debug";

            public string CommitSha => CSemVer.InformationalVersion.ZeroCommitSha;

            public DateTime CommitDateUtc => CSemVer.InformationalVersion.ZeroCommitDate;

            public string InformationalVersion => CSemVer.InformationalVersion.ZeroInformationalVersion;

            public SVersion SemVersion => SVersion.ZeroVersion;

            public SVersion NuGetVersion => SVersion.ZeroVersion;

            public string AssemblyVersion => CSemVer.InformationalVersion.ZeroAssemblyVersion;

            public string FileVersion => CSemVer.InformationalVersion.ZeroFileVersion;
        }

        /// <summary>
        /// A Zero Version <see cref="ICommitAssemblyBuildInfo"/>.
        /// </summary>
        public static readonly ICommitAssemblyBuildInfo ZeroBuildInfo = new ZeroBuild();

        /// <summary>
        /// Initializes a new <see cref="CommitAssemblyBuildInfo"/> for a release version.
        /// </summary>
        /// <param name="releaseVersion">The release version.</param>
        /// <param name="commitSha">The commit sha.</param>
        /// <param name="commitDateUtc">The commit date Utc.</param>
        public CommitAssemblyBuildInfo( CSVersion releaseVersion, string commitSha, DateTime commitDateUtc )
        {
            if( releaseVersion == null || !releaseVersion.IsValid ) throw new ArgumentNullException( nameof( releaseVersion ) );
            if( String.IsNullOrWhiteSpace( commitSha ) ) throw new ArgumentNullException( nameof( commitSha ) );
            if( commitDateUtc.Kind != DateTimeKind.Utc ) throw new ArgumentException( "Must be Utc.", nameof( commitSha ) );

            // Take no risk: normalize the raw text.
            NuGetVersion = SVersion.Parse( releaseVersion.ToString( CSVersionFormat.NuGetPackage ) );
            SemVersion = SVersion.Parse( releaseVersion.ToString( CSVersionFormat.Normalized ) );

            CommitDateUtc = commitDateUtc;
            CommitSha = commitSha;

            BuildConfiguration = releaseVersion.Prerelease.Length == 0 || releaseVersion.Prerelease == "rc"
                                   ? "Release"
                                   : "Debug";

            AssemblyVersion = $"{releaseVersion.Major}.{releaseVersion.Minor}";
            FileVersion = releaseVersion.ToStringFileVersion( false );

            InformationalVersion = CSemVer.InformationalVersion.BuildInformationalVersion( SemVersion.ToString(), NuGetVersion.ToString(), CommitSha, CommitDateUtc );
        }

        /// <summary>
        /// Gets "Debug" for ci build or prerelease below "rc" and "Release" for "rc" and official releases.
        /// Never null, defaults to "Debug".
        /// </summary>
        public string BuildConfiguration { get; }

        /// <summary>
        /// Gets the Sha of the commit.
        /// Defaults to <see cref="InformationalVersion.ZeroCommitSha"/>.
        /// </summary>
        public string CommitSha { get; }

        /// <summary>
        /// Gets the UTC date and time of the commit.
        /// Defaults to <see cref="InformationalVersion.ZeroCommitDate"/>.
        /// </summary>
        public DateTime CommitDateUtc { get; }

        /// <summary>
        /// Gets the standardized information version string that must be used to build this
        /// commit point. Never null: defaults to <see cref="InformationalVersion.ZeroInformationalVersion"/>.
        /// string.
        /// </summary>
        public string InformationalVersion { get; }

        /// <summary>
        /// Gets the semantic version (long form) that must be used to build this commit
        /// point. Never null: defaults to <see cref="SVersion.ZeroVersion"/>.
        /// </summary>
        public SVersion SemVersion { get; }

        /// <summary>
        /// Gets the NuGet version (short form) that must be used to build this commit point.
        /// Never null: defaults to <see cref="SVersion.ZeroVersion"/>.
        /// </summary>
        public SVersion NuGetVersion { get; }

        /// <summary>
        /// Gets the "Major.Minor" string.
        /// Never null, defaults to "0.0".
        /// </summary>
        public string AssemblyVersion { get; }

        /// <summary>
        /// Gets the 'Major.Minor.Build.Revision' windows file version to use based on the <see cref="CSVersion.OrderedVersion"/>.
        /// When it is a release the last part (Revision) is even and it is odd for CI builds. 
        /// Defaults to '0.0.0.0' (<see cref="InformationalVersion.ZeroFileVersion"/>).
        /// See <see cref="CSVersion.ToStringFileVersion(bool)"/>.
        /// </summary>
        public string FileVersion { get; }
    }

}