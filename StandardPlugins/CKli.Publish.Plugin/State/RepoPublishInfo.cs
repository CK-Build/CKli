using CK.Core;
using CKli.Core;
using CSemVer;
using CKli.ArtifactHandler.Plugin;

namespace CKli.Publish.Plugin;

sealed class RepoPublishInfo
{
    readonly Repo _repo;
    readonly string _branchName;
    readonly int _index;
    readonly SVersion _baseVersion;
    readonly SVersion _publishVersion;
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

    internal RepoPublishInfo( Repo repo,
                              string branchName,
                              int index,
                              SVersion baseVersion,
                              SVersion publishVersion,
                              BuildContentInfo buildContentInfo )
    {
        _repo = repo;
        _branchName = branchName;
        _index = index;
        _baseVersion = baseVersion;
        _publishVersion = publishVersion;
        _buildContentInfo = buildContentInfo;
    }

    internal RepoPublishInfo( int index, string branchName, SVersion baseVersion, BuildResult result )
        : this( result.Repo, branchName, index, baseVersion, result.Version, result.Content )
    {
    }
}
