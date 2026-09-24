using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;

using LibGit2Sharp;
using System;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Captures a commit and everything required to build it in a given version.
/// </summary>
public sealed class CommitBuildInfo
{
    readonly VersionTagInfo _tagInfo;
    readonly SVersion _version;
    readonly Commit _buildCommit;
    readonly string _toString;
    string? _informationalVersion;

    internal CommitBuildInfo( VersionTagInfo tagInfo, SVersion version, Commit buildCommit )
    {
        _tagInfo = tagInfo;
        _version = version;
        _buildCommit = buildCommit;
        _toString = $"{tagInfo.Repo.DisplayPath}/v{version}";
    }

    /// <summary>
    /// Gets the concerned Repo.
    /// </summary>
    public Repo Repo => _tagInfo.Repo;

    /// <summary>
    /// Gets the version to build. The <see cref="SVersion.ParsedPrefix"/> is "local/" except when rebuilding an
    /// existing published tag.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the build commit.
    /// </summary>
    public Commit BuildCommit => _buildCommit;

    /// <summary>
    /// Gets the informational version (see <see cref="InformationalVersion"/>) that must be embedded in the NuGet packages.
    /// </summary>
    public string InformationalVersion
    {
        get
        {
            return _informationalVersion ??= CK.Core.InformationalVersion.BuildInformationalVersion( _version,
                                                                                                     _buildCommit.Sha,
                                                                                                     _buildCommit.Committer.When.UtcDateTime );
        }
    }

    /// <summary>
    /// Gets the 'Major.Minor.Build.Revision' windows file version to use.
    /// This is currently not used and defaults to '0.0.0.0' (<see cref="InformationalVersion.ZeroFileVersion"/>).
    /// </summary>
    public string FileVersion => CK.Core.InformationalVersion.ZeroFileVersion;

    /// <summary>
    /// Gets whether the build must use "Release" configuration (see <see cref="IsReleaseConfiguration(SVersion)"/>).
    /// </summary>
    public bool ReleaseConfiguration => IsReleaseConfiguration( _version );

    /// <summary>
    /// Gets whether a version must be built in "Release" configuration: it is not a CI version
    /// (<see cref="SVersion.IsCI"/> versions are always built in "Debug") and it is a <see cref="CSVersionKind.Stable"/>,
    /// a conformant prerelease from <see cref="CSVersionKind.Romeo"/> to <see cref="CSVersionKind.Zulu"/>
    /// or a <see cref="CSVersionKind.Exploratory"/>.
    /// </summary>
    /// <param name="version">The version to build.</param>
    /// <returns>True for "Release", false for "Debug".</returns>
    public static bool IsReleaseConfiguration( SVersion version )
    {
        return !version.IsCI && version.VersionKind is CSVersionKind.Exploratory or >= CSVersionKind.Romeo;
    }

    /// <summary>
    /// Sets this <see cref="Version"/> as a tag on <see cref="BuildCommit"/>.
    /// <para>
    /// This ends a build session and nothing is done to synchronize the <see cref="VersionTagInfo.AllTagCommits"/> collection:
    /// the state of the system doesn't reflect the repository anymore.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The context.</param>
    /// <param name="contentInfo">The build content info.</param>
    /// <returns>The non null tag and version on success.</returns>
    public (Tag? T, SVersion? V) ApplyReleaseBuildTag( IActivityMonitor monitor,
                                                       CKliEnv context,
                                                       BuildContentInfo contentInfo )
    {
        var vTag = _version.IsLocal()
                    ? $"local/v{_version}"
                    : _version.IsBuilding()
                        ? $"building/v{_version}"
                        : $"v{_version}";

        monitor.Info( $"""
                Setting build tag '{vTag}' on '{Repo.DisplayPath}' (commit: '{_buildCommit}'):
                {contentInfo}
                """ );
        try
        {
            // Creates the tag (with or without "building/" or "local/" prefix). If this exact tag is on another
            // commit, it is moved (allowOverwrite: true).
            var git = _tagInfo.Repo.GitRepository.Repository;
            var t = git.Tags.Add( vTag,
                                  _buildCommit,
                                  context.Committer,
                                  contentInfo.ToString(),
                                  allowOverwrite: true );
            if( t == null )
            {
                monitor.Error( GetErrorMessage( contentInfo, vTag ) );
                return (null, null);
            }
            // The "building/" and "local/" prefixes are two STATES of one version tag, not two tags: a version
            // is borne by a single commit. Tags.Add above moves its own name (allowOverwrite: true), but a state
            // change renames the tag, so a same-version tag under the other prefix must be dropped here.
            //
            // Without this, a build that fails before BuildResult.CommitBuilding promotes "building/vX" to
            // "local/vX" leaves the "building/vX" it just wrote BESIDE the "local/vX" it was rolling: one
            // version on two commits, which no longer loads (the version to commit index cannot hold both).
            // The surviving "building/vX" is the useful half - it says where a build was attempted and did not
            // complete - and the next roadmap that reaches a build promotes it in place: RoadmapExecutor's
            // promotion loop walks every OrderedSolutions entry, not only the ones it rebuilt.
            //
            // This removes the TAG only, never through DestroyLocalRelease: that one also purges the tag's
            // produced packages from the local feed and the NuGet cache, and PublishToNuGetLocalFeed has just
            // written this very version there. (It is also why the filter below is "v != _version".)
            DropOtherStateTag( monitor, git, $"building/v{_version}", vTag );
            DropOtherStateTag( monitor, git, $"local/v{_version}", vTag );
            // Destroys any other (previous!) building or local releases with the same branch name.
            // Note:
            //     |  First idea was to add these conditions:
            //     |     - always if they are CI (because whatever we just built, it is "better" than an old CI).
            //     |     - if they are not CI, then we destroy them only if we just built a new non-CI version.
            //     |  This would have preserved non-CI builds in presence of CI builds.
            //   But having 2 sets of local versions introduces major ambiguities (technically but also
            //   for the user). So we decide to ignore the CI/non-CI aspect: a "local/" always replaces the
            //   previously built "local/" version.
            _tagInfo.DestroyLocalReleases( monitor, v => v != _version && v.BranchName == _version.BranchName );
            return (t,_version);
        }
        catch( Exception ex )
        {
            // This should be a "World.Problem"
            // Problems may be future new beasts that are serializable proto/persistent-issues with a
            // "bool StillApply( ... out World.Issue issue )". 
            monitor.Error( GetErrorMessage( contentInfo, vTag ), ex );
            return (null,null);
        }

        // Only the unpublished states are ever dropped: a published "v{version}" tag is never touched here.
        static void DropOtherStateTag( IActivityMonitor monitor, Repository git, string otherName, string vTag )
        {
            if( otherName == vTag ) return;
            var other = git.Tags[otherName];
            if( other != null )
            {
                monitor.Info( $"Removing the superseded '{otherName}' tag: '{vTag}' is now the state of this version." );
                git.Tags.Remove( other );
            }
        }

        string GetErrorMessage( BuildContentInfo contentInfo, string vTag )
        {
            return $"""
                    Unable to apply tag '{vTag}' in '{Repo.DisplayPath}' on commit '{_buildCommit.Sha.AsSpan( 0, 7 )} {_buildCommit.MessageShort}' with content:
                    {contentInfo}
                    """;
        }
    }

    /// <summary>
    /// Gets "Repo/version".
    /// </summary>
    /// <returns>The "Repo/version" that identifies this build info.</returns>
    public override string ToString() => _toString;
}

