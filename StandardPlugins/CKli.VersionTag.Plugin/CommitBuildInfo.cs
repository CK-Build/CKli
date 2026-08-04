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
    readonly bool _rebuildingCommit;
    readonly bool _rebuildingVersion;
    string? _informationalVersion;

    internal CommitBuildInfo( VersionTagInfo tagInfo, SVersion version, Commit buildCommit, bool rebuildingCommit, bool rebuildingVersion )
    {
        _tagInfo = tagInfo;
        _version = version;
        _buildCommit = buildCommit;
        _rebuildingCommit = rebuildingCommit;
        _rebuildingVersion = rebuildingVersion;
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
    /// Gets whether we are rebuilding an existing commit.
    /// </summary>
    public bool RebuildingCommit => _rebuildingCommit;

    /// <summary>
    /// Gets whether we are rebuilding an existing version.
    /// </summary>
    public bool RebuildingVersion => _rebuildingVersion;

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
    /// Gets whether the build must use "Release" configuration: the version to build is a
    /// <see cref="CSVersionKind.Stable"/> or a conformant prerelease from <see cref="CSVersionKind.Romeo"/>
    /// to <see cref="CSVersionKind.Zulu"/>.
    /// </summary>
    public bool ReleaseConfiguration => _version.VersionKind >= CSVersionKind.Romeo;

    /// <summary>
    /// Adds or update the <see cref="TagCommit"/> on the <see cref="BuildCommit"/> for <see cref="Version"/>
    /// with the provided <paramref name="contentInfo"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The context.</param>
    /// <param name="contentInfo">The build content info.</param>
    /// <returns>The non null tag and version on success.</returns>
    public (Tag? T, SVersion? V) ApplyReleaseBuildTag( IActivityMonitor monitor,
                                                       CKliEnv context,
                                                       BuildContentInfo contentInfo )
    {
        // When rebuilding, the _version is not "local/".
        var vPublishedTag = $"v{_version}";
        var vLocalTag = $"local/{vPublishedTag}";
        var vTag = _version.IsLocal() ? vLocalTag : vPublishedTag;

        monitor.Info( $"""
                Setting build tag '{vTag}' on '{Repo.DisplayPath}' (commit: '{_buildCommit}'):
                {contentInfo}
                """ );
        try
        {
            // Creates the tag (with or without "local/" prefix). If this exact tag is on another
            // commit, it is moved (allowOverwrite: true).
            var git = _tagInfo.Repo.GitRepository.Repository;
            var t = git.Tags.Add( vTag,
                                  _buildCommit,
                                  context.Committer,
                                  contentInfo.ToString(),
                                  allowOverwrite: true );

            //    TODO: investigate whether TagCommits' updates are actually needed...
            if( _tagInfo.TryGetTagCommit( _version, out var exists ) )
            {
                // A TagCommit exists with the version.
                // - ci.0 case: update the TagCommit with the new ci.0 tag.
                // - other cases:
                // If the existing tag is not exactly the same (but is not a +fake), we remove the tag (this removes a "published"
                // tag if it exists) and then we update the TagCommit:
                //   - If on the same commit, the TagCommit's tag is set to the new one.
                //   - If on a different commit, we remove the TagCommit and recreate a new one on the buildCommit.
                //
                // => This is NOT perfect in terms of TagCommits but we don't really care: once built, the VersionTagInfo
                //    is not used anymore. The really important aspect here is to update the "real" git tags,
                //    not to maintain the TagCommits' state.


                Throw.DebugAssert( "We must not be able to rebuild a +deprecated commit.", !exists.IsDeprecatedVersion );
                Throw.DebugAssert( """
                                   When rebuilding an existing version, the build commit must be the same, except if:
                                   - the existing tag is a +fake.
                                   - or the tag is a "local/" (the tag moves to the "current" commit).
                                   """,
                                   exists.IsFakeVersion || exists.Version.IsLocal() || _buildCommit.Sha == exists.Sha );
                if( _version.CINumber == 0 )
                {
                    // "--ci.0" case: we must be on the same original non-CI build commit.
                    Throw.DebugAssert( "We are on the base version commit.", exists.Commit.Sha == _buildCommit.Sha );
                    exists.SetCI0VersionTag( t, _version );
                }
                else
                {
                    // We don't want to remove a +fake git tag (this one coexists with its regular counterparts).
                    if( !exists.IsFakeVersion && exists.Tag.CanonicalName != t.CanonicalName )
                    {
                        // Removes the other ("local/" vs. published), tag (may be on the same commit or not).
                        git.Tags.Remove( exists.Tag.CanonicalName );
                    }
                    // 
                    if( exists.Commit.Sha == _buildCommit.Sha )
                    {
                        exists.UpdateVersionTag( t );
                    }
                    else
                    {
                        _tagInfo.RemoveTagCommit( monitor, _version );
                        _tagInfo.AddReleaseBuildTag( _version, _buildCommit, t, contentInfo );
                    }
                }
            }
            else
            {
                Throw.DebugAssert( "We are not on a 'ci.0' version (the commit would have been found).", _version.CINumber != 0 );
                _tagInfo.AddReleaseBuildTag( _version, _buildCommit, t, contentInfo );
            }

            // Destroys any other (previous!) local releases with the same branch name and:
            // - always if they are CI (because whatever we just built, it is "better" than an old CI).
            // - if they are not CI, then we destroy them only if we just built a new non-CI version.
            _tagInfo.DestroyLocalReleases( monitor, v => v != _version
                                                         && v.BranchName == _version.BranchName
                                                         && (v.IsCI || !_version.IsCI) );
            return (t,_version);
        }
        catch( Exception ex )
        {
            // This should be a "World.Problem"
            // Problems may be future new beasts that are serializable proto/persistent-issues with a
            // "bool StillApply( ... out World.Issue issue )". 
            monitor.Error( $"""
                Unexpecting error while applying '{vTag}' on '{Repo.DisplayPath}' (commit: '{_buildCommit.Sha}') with content:
                {contentInfo}
                """, ex );
            return (null,null);
        }
    }

    /// <summary>
    /// Gets "Repo/version".
    /// </summary>
    /// <returns>The "Repo/version" that identifies this build info.</returns>
    public override string ToString() => _toString;
}

