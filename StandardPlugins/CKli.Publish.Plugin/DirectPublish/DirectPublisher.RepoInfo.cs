using CK.Core;
using CKli.Core;

using CKli.ArtifactHandler.Plugin;
using LibGit2Sharp;
using System.Collections.Immutable;

namespace CKli.Publish.Plugin;

sealed partial class DirectPublisher
{
    public sealed class RepoInfo
    {
        readonly Repo _repo;
        readonly string _branchName;
        readonly int _index;
        readonly SVersion _publishVersion;
        readonly Tag _publishTag;
        readonly BuildContentInfo _buildContentInfo;
        readonly ImmutableArray<string> _branchPushRefSpecs;

        /// <summary>
        /// Gets the Repo.
        /// </summary>
        public Repo Repo => _repo;

        /// <summary>
        /// Gets the branch name. This is a "dev/XXX" branch when <see cref="WorldReleaseInfo.IsCIBuild"/> is true.
        /// This is a "fix/v..." branch for the fix workflow.
        /// <para>
        /// This branch will be pushed with all the registered <see cref="GitRepository.DeferredPushRefSpecs"/> after
        /// the draft release has been created on the remote (see <see cref="GitHostingProvider.CreateDraftReleaseAsync"/>).
        /// </para>
        /// </summary>
        public string BranchName => _branchName;

        /// <summary>
        /// Gets the index of this published repository in its <see cref="DirectPublisher.Repos"/>.
        /// </summary>
        public int Index => _index;

        /// <summary>
        /// Gets the content that must be published.
        /// </summary>
        public BuildContentInfo BuildContentInfo => _buildContentInfo;

        /// <summary>
        /// Gets the number of "items" to publish: the <see cref="BuildContentInfo.Produced"/> packages plus the <see cref="BuildContentInfo.AssetFileNames"/> files
        /// plus two for the start and end of the <see cref="Repo"/> itself.
        /// </summary>
        public int PublishedLength => 1 + _buildContentInfo.Produced.Length + _buildContentInfo.AssetFileNames.Length + 1;

        /// <summary>
        /// Gets the version to publish for this repository.
        /// </summary>
        public SVersion PublishVersion => _publishVersion;

        /// <summary>
        /// Gets the tag that contains the version to publish for this repository.
        /// </summary>
        public Tag PublishTag => _publishTag;

        /// <summary>
        /// Gets the <see cref="GitRepository.DeferredPushRefSpecs"/> to add before pushing <see cref="BranchName"/>.
        /// </summary>
        public ImmutableArray<string> BranchPushRefSpecs => _branchPushRefSpecs;

        internal RepoInfo( Repo repo,
                           string branchName,
                           int index,
                           SVersion publishVersion,
                           Tag publishTag,
                           BuildContentInfo buildContentInfo,
                           ImmutableArray<string> branchPushRefSpecs )
        {
            Throw.DebugAssert( !branchPushRefSpecs.IsDefault );
            _repo = repo;
            _branchName = branchName;
            _index = index;
            _publishVersion = publishVersion;
            _publishTag = publishTag;
            _buildContentInfo = buildContentInfo;
            _branchPushRefSpecs = branchPushRefSpecs;
        }

        internal RepoInfo( int index, string branchName, BuildResult result )
            : this( result.Repo, branchName, index, result.Version, result.VersionTag, result.Content, [] )
        {
        }
    }
}
