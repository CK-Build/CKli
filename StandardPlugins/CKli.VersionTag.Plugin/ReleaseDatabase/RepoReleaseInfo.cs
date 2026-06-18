using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using System.Collections.Generic;
using System.Diagnostics;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Captures the information for the release of a <see cref="Repo"/> in a <see cref="Version"/>.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class RepoReleaseInfo
{
    readonly VersionTagPlugin.ReleaseDatabase _releaseDatabase;
    readonly RepoKey _repoKey;
    readonly BuildContentInfo _buildContentInfo;
    readonly List<RepoReleaseInfo> _directProducers;
    readonly HashSet<RepoReleaseInfo> _allProducers;
    IReadOnlyList<RepoReleaseInfo>? _directConsumers;

    internal RepoReleaseInfo( VersionTagPlugin.ReleaseDatabase releaseDatabase,
                              RepoKey repoKey,
                              BuildContentInfo buildContentInfo,
                              List<RepoReleaseInfo> directProducers,
                              HashSet<RepoReleaseInfo> allProducers )
    {
        _releaseDatabase = releaseDatabase;
        _repoKey = repoKey;
        _buildContentInfo = buildContentInfo;
        _directProducers = directProducers;
        _allProducers = allProducers;
    }

    /// <summary>
    /// Gets the released repository.
    /// </summary>
    public Repo Repo => _repoKey.Repo;

    /// <summary>
    /// Gets the released version.
    /// </summary>
    public SVersion Version => _repoKey.Version;

    /// <summary>
    /// Gets the Repo's release content.
    /// </summary>
    public BuildContentInfo Content => _buildContentInfo;

    /// <summary>
    /// Gets the direct producers of this release.
    /// </summary>
    public IReadOnlyList<RepoReleaseInfo> DirectProducers => _directProducers;

    /// <summary>
    /// Gets the closure of all producers of this release.
    /// </summary>
    public IReadOnlySet<RepoReleaseInfo> AllProducers => _allProducers;

    /// <summary>
    /// Gets whether all <see cref="BuildContentInfo.Produced"/> NuGet packages are in "$Local/&lt;world name&gt;/NuGet"
    /// and all <see cref="BuildContentInfo.AssetFileNames"/> are in their folder.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="assetsFolder">
    /// Outputs the "$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;" where artifacts are.
    /// This is <see cref="NormalizedPath.IsEmptyPath"/> if <see cref="BuildContentInfo.AssetFileNames"/> is empty.
    /// </param>
    /// <returns>True if all the assets are locally available.</returns>
    public bool HasAllLocalArtifacts( IActivityMonitor monitor, out NormalizedPath assetsFolder )
    {
        return _releaseDatabase.ArtifactHandlerPlugin.HasAllArtifacts( monitor, Repo, Version, Content, out assetsFolder );
    }

    /// <summary>
    /// Gets the direct consumers of this release.
    /// <para>
    /// This list is built on demand and cached.
    /// </para>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <returns>The list of direct consumers.</returns>
    public IReadOnlyList<RepoReleaseInfo> GetDirectConsumers( IActivityMonitor monitor )
    {
        return _directConsumers ??= _releaseDatabase.GetDirectConsumers( monitor, this );
    }

    /// <summary>
    /// Overridden to return the Repo display path and the released version.
    /// The format is "Repo/v{Version}" that intentionally differs from the <see cref="PackageInstance.ToString()"/>.
    /// </summary>
    /// <returns>Repo display path/v{Released version}.</returns>
    public override string ToString() => $"{Repo.DisplayPath}/v{Version}";

}
