using CSemVer;
using System;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Immutable encapsulation of all we need to know in terms of versions in a Git
    /// repository to reason on new versions or to generate a build thanks to <see cref="FinalBuildInfo"/>.
    /// </summary>
    public class CommitVersionInfo
    {
        /// <summary>
        /// Initializes a new valid <see cref="CommitVersionInfo"/>.
        /// </summary>
        /// <param name="commitSHA1">The commit SHA1. Must not be null.</param>
        /// <param name="releaseVersion">See <see cref="ValidReleaseTag"/>.</param>
        /// <param name="releaseContentVersion">See <see cref="BetterExistingVersion"/>.</param>
        /// <param name="previousVersion">See <see cref="BestCommitBelow"/>.</param>
        /// <param name="nextPossibleVersions">See <see cref="NextPossibleVersions"/>.</param>
        /// <param name="possibleVersions">See <see cref="PossibleVersions"/>.Must not be null.</param>
        /// <param name="assemblyBuildInfo">See <see cref="FinalBuildInfo"/>. Must not be null.</param>
        public CommitVersionInfo(
            string commitSha,
            CSVersion releaseVersion,
            CSVersion releaseContentVersion,
            CSVersion previousVersion,
            string previousVersionCommitSha,
            IReadOnlyList<CSVersion> nextPossibleVersions,
            IReadOnlyList<CSVersion> possibleVersions,
            ICommitBuildInfo assemblyBuildInfo )
        {
            CommitSha = commitSha ?? throw new ArgumentNullException( nameof( commitSha ) );
            ValidReleaseTag = releaseVersion;
            BetterExistingVersion = releaseContentVersion;
            if( (previousVersion == null) != (previousVersionCommitSha == null) )
            {
                if( previousVersion == null )
                {
                    throw new ArgumentException( "Must be null.", nameof( previousVersionCommitSha ) );
                }
                throw new ArgumentNullException( nameof( previousVersionCommitSha ) );
            }
            BestCommitBelow = previousVersion;
            BestCommitBelowSha = previousVersionCommitSha;
            NextPossibleVersions = nextPossibleVersions ?? throw new ArgumentNullException( nameof( nextPossibleVersions ) );
            PossibleVersions = possibleVersions ?? throw new ArgumentNullException( nameof( possibleVersions ) );
            FinalBuildInfo = assemblyBuildInfo ?? throw new ArgumentNullException( nameof( assemblyBuildInfo ) );
        }

        /// <summary>
        /// Initilalizes a new invalid CommitVersionInfo.
        /// <see cref="FinalBuildInfo"/> is the <see cref="CommitAssemblyBuildInfo.ZeroBuildInfo"/>.
        /// </summary>
        public CommitVersionInfo()
        {
            FinalBuildInfo = CommitAssemblyBuildInfo.ZeroBuildInfo;
        }

        /// <summary>
        /// Gets whether this commit info is valid.
        /// </summary>
        public bool IsValid => CommitSha != null;

        /// <summary>
        /// Gets this commit's SHA1.
        /// Null when <see cref="IsValid"/> is false.
        /// </summary>
        public string CommitSha { get; }

        /// <summary>
        /// Gets the version directly associated to this commit.
        /// This is null if there is actually no release tag on the current commit or if <see cref="IsValid"/> is false.
        /// (This is the RepositoryInfo.ValidReleaseTag from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public CSVersion ValidReleaseTag { get; }

        /// <summary>
        /// Gets the version associated to the commit content.
        /// (This is the RepositoryInfo.BetterExistingVersion.ThisTag from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public CSVersion BetterExistingVersion { get; }

        /// <summary>
        /// Gets the previous version, associated to a commit below the current one.
        /// This is null if no previous version has been found.
        /// (This is the RepositoryInfo.CommitInfo.BasicInfo?.BestCommitBelow.ThisTag from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public CSVersion BestCommitBelow { get; }

        /// <summary>
        /// Gets the previous version commit Sha, associated to a commit below the current one.
        /// This is null if no previous version has been found.
        /// (This is the RepositoryInfo.CommitInfo.BasicInfo?.BestCommitBelow.CommitSha from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public string BestCommitBelowSha { get; }

        /// <summary>
        /// Get the versions that may be available to any commit above the current one.
        /// Null if <see cref="IsValid"/> is false.
        /// (This is the RepositoryInfo.NextPossibleVersions from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public IReadOnlyList<CSVersion> NextPossibleVersions { get; }

        /// <summary>
        /// Gets the possible versions for the current commit point.
        /// Null if <see cref="IsValid"/> is false.
        /// When empty, this means that there can not be a valid release tag on the current commit point.
        /// (This is the RepositoryInfo.PossibleVersions from SimpleGitVersion.RepositoryInfo.)
        /// </summary>
        public IReadOnlyList<CSVersion> PossibleVersions { get; }

        /// <summary>
        /// Gets the <see cref="ICommitBuildInfo"/> for this commit point.
        /// Never null, defaults to <see cref="CommitAssemblyBuildInfo.ZeroBuildInfo"/>.
        /// </summary>
        public ICommitBuildInfo FinalBuildInfo { get; }
    }
}
