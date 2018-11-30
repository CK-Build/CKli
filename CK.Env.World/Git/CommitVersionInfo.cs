using CSemVer;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Immutable encapsulation of all we need to know in terms of versions in a Git
    /// repository to reason on new versions.
    /// </summary>
    public class CommitVersionInfo
    {
        /// <summary>
        /// Initializes a new <see cref="CommitVersionInfo"/>.
        /// </summary>
        /// <param name="commitSHA1">The commit SHA1.</param>
        /// <param name="releaseVersion">See <see cref="ReleaseVersion"/>.</param>
        /// <param name="releaseContentVersion">See <see cref="ReleaseContentVersion"/>.</param>
        /// <param name="previousVersion">See <see cref="PreviousVersion"/>.</param>
        /// <param name="nextPossibleVersions">See <see cref="NextPossibleVersions"/>.</param>
        /// <param name="possibleVersions">See <see cref="PossibleVersions"/>.</param>
        public CommitVersionInfo(
            string commitSha,
            CSVersion releaseVersion,
            CSVersion releaseContentVersion,
            CSVersion previousVersion,
            IReadOnlyList<CSVersion> nextPossibleVersions,
            IReadOnlyList<CSVersion> possibleVersions )
        {
            CommitSha = commitSha ?? throw new ArgumentNullException( nameof( commitSha ) );
            ReleaseVersion = releaseVersion;
            ReleaseContentVersion = releaseContentVersion;
            PreviousVersion = previousVersion;
            NextPossibleVersions = nextPossibleVersions ?? throw new ArgumentNullException( nameof( nextPossibleVersions ) );
            PossibleVersions = possibleVersions ?? throw new ArgumentNullException( nameof( possibleVersions ) );
        }

        /// <summary>
        /// Gets this commit's SHA1.
        /// </summary>
        public string CommitSha { get; }

        /// <summary>
        /// Gets the version directly associated to this commit.
        /// This is null if there is actually no release tag on the current commit.
        /// (This is the RepositoryInfo.ValidReleaseTag from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public CSVersion ReleaseVersion { get; }

        /// <summary>
        /// Gets the version associated to the commit content.
        /// (This is the RepositoryInfo.BetterExistingVersion.ThisTag from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public CSVersion ReleaseContentVersion { get; }

        /// <summary>
        /// Gets the previous version, associated to a commit below the current one.
        /// (This is the RepositoryInfo.CommitInfo.BasicInfo?.BestCommitBelow.ThisTag from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public CSVersion PreviousVersion { get; }

        /// <summary>
        /// Get the versions that may be available to any commit above the current one.
        /// (This is the RepositoryInfo.NextPossibleVersions from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public IReadOnlyList<CSVersion> NextPossibleVersions { get; }

        /// <summary>
        /// Gets the possible versions for the current commit point.
        /// When empty, this means that there can not be a valid release tag on the current commit point.
        /// (This is the RepositoryInfo.PossibleVersions from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public IReadOnlyList<CSVersion> PossibleVersions { get; }

    }
}
