using System;
using CSemVer;

namespace CK.Env
{
    /// <summary>
    /// Exposes all information version required to build a project.
    /// Implementation should default to Zero Version if anything prevents computation
    /// and 
    /// </summary>
    public interface ICommitAssemblyBuildInfo
    {
        /// <summary>
        /// Gets "Debug" for ci build or prerelease below "rc" and "Release" for "rc" and official releases.
        /// Never null, defaults to "Debug".
        /// </summary>
        string BuildConfiguration { get; }

        /// <summary>
        /// Gets the Sha of the commit.
        /// Defaults to <see cref="InformationalVersion.ZeroCommitSha"/>.
        /// </summary>
        string CommitSha { get; }

        /// <summary>
        /// Gets the UTC date and time of the commit.
        /// Defaults to <see cref="InformationalVersion.ZeroCommitDate"/>.
        /// </summary>
        DateTime CommitDateUtc { get; }

        /// <summary>
        /// Gets the standardized information version string that must be used to build this
        /// commit point. Never null: defaults to <see cref="InformationalVersion.ZeroInformationalVersion"/>.
        /// string.
        /// </summary>
        string InformationalVersion { get; }

        /// <summary>
        /// Gets the semantic version (long form) that must be used to build this commit
        /// point. Never null: defaults to <see cref="SVersion.ZeroVersion"/>.
        /// </summary>
        SVersion SemVersion { get; }

        /// <summary>
        /// Gets the NuGet version (short form) that must be used to build this commit point.
        /// Never null: defaults to <see cref="SVersion.ZeroVersion"/>.
        /// </summary>
        SVersion NuGetVersion { get; }

        /// <summary>
        /// Gets the "Major.Minor" string.
        /// Never null, defaults to "0.0".
        /// </summary>
        string AssemblyVersion { get; }

        /// <summary>
        /// Gets the 'Major.Minor.Build.Revision' windows file version to use based on the <see cref="CSVersion.OrderedVersion"/>.
        /// When it is a release the last part (Revision) is even and it is odd for CI builds. 
        /// Defaults to '0.0.0.0' (<see cref="InformationalVersion.ZeroFileVersion"/>).
        /// See <see cref="CSVersion.ToStringFileVersion(bool)"/>.
        /// </summary>
        string FileVersion { get; }
    }

    /// <summary>
    /// Extends <see cref="ICommitAssemblyBuildInfo"/>.
    /// </summary>
    public static class CommitAssemblyBuildInfoExtension
    {
        /// <summary>
        /// Returns a <see cref="ICommitAssemblyBuildInfo"/> for a release version.
        /// </summary>
        /// <param name="this">This build info.</param>
        /// <param name="release">The release version.</param>
        /// <returns>This or a new build info.</returns>
        public static ICommitAssemblyBuildInfo WithReleaseVersion( this ICommitAssemblyBuildInfo @this, CSVersion release )
        {
            if( release == null ) throw new ArgumentNullException( nameof( release ) );
            return @this.NuGetVersion == release || @this.SemVersion == release
                    ? @this
                    : new CommitAssemblyBuildInfo( release, @this.CommitSha, @this.CommitDateUtc );
        }
    }
}
