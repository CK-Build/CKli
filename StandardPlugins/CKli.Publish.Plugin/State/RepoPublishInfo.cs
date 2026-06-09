using CK.Core;
using CKli.Core;

using CKli.ArtifactHandler.Plugin;
using LibGit2Sharp;

namespace CKli.Publish.Plugin;

sealed class RepoPublishInfo
{
    readonly Repo _repo;
    readonly string _branchName;
    readonly int _index;
    readonly SVersion _publishVersion;
    readonly Tag _publishTag;
    readonly BuildContentInfo _buildContentInfo;

    /// <summary>
    /// Gets the Repo.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the branch name. This is a "dev/XXX" branch when <see cref="WorldReleaseInfo.IsCIBuild"/> is true.
    /// </summary>
    public string BranchName => _branchName;

    /// <summary>
    /// Gets the index of this published repository in its <see cref="WorldReleaseInfo.Repos"/>.
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

    internal RepoPublishInfo( Repo repo,
                              string branchName,
                              int index,
                              SVersion publishVersion,
                              Tag publishTag,
                              BuildContentInfo buildContentInfo )
    {
        _repo = repo;
        _branchName = branchName;
        _index = index;
        _publishVersion = publishVersion;
        _publishTag = publishTag;
        _buildContentInfo = buildContentInfo;
    }

    internal RepoPublishInfo( int index, string branchName, BuildResult result )
        : this( result.Repo, branchName, index, result.Version, result.VersionTag, result.Content )
    {
    }
}
