using CK.Core;
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
    readonly bool _rebuilding;
    string? _informationalVersion;

    internal CommitBuildInfo( VersionTagInfo tagInfo, SVersion version, Commit buildCommit, bool rebuilding )
    {
        Throw.DebugAssert( version.ParsedPrefix == "local/" );
        _tagInfo = tagInfo;
        _version = version;
        _buildCommit = buildCommit;
        _rebuilding = rebuilding;
        _toString = $"{tagInfo.Repo.DisplayPath}/{version}";
    }

    /// <summary>
    /// Gets the concerned Repo.
    /// </summary>
    public Repo Repo => _tagInfo.Repo;

    /// <summary>
    /// Gets the version to build. The <see cref="SVersion.ParsedPrefix"/> is "local/".
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the build commit.
    /// </summary>
    public Commit BuildCommit => _buildCommit;

    /// <summary>
    /// Gets whether we are rebuilding an existing version.
    /// </summary>
    public bool Rebuilding => _rebuilding;

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
    /// with the provided <paramref name="releaseMessage"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The context.</param>
    /// <param name="releaseMessage">The non empty release message.</param>
    /// <returns>True on success, false on error.</returns>
    public TagCommit? ApplyReleaseBuildTag( IActivityMonitor monitor,
                                            CKliEnv context,
                                            string releaseMessage )
    {
        Throw.CheckArgument( !string.IsNullOrWhiteSpace( releaseMessage ) );
        // Local fix builds have no release tag. If the release local database is reset, we lose them
        // but this is not an issue, this is used as an optimization that avoids rebuilding origins when
        // an impacted repo needs to be rebuilt.
        // => Skipped local fix.
        bool isLocalFix = _version.IsLocalFix();
        monitor.Info( $"""
                    {(isLocalFix
                ? "Not setting (local fix build are only registered in the local release database)"
                : "Setting")} build tag 'local/v{_version}' on '{Repo.DisplayPath}' (commit: {_buildCommit.Sha}):
                    {releaseMessage}
                    """ );
        if( isLocalFix )
        {
            throw new NotSupportedException( "LocalFix is no more supported." );
        }
        try
        {
            // Our _version is "local/" but the version tag may already exist with or without "local/" prefix.
            var git = _tagInfo.Repo.GitRepository.Repository;
            var t = git.Tags.Add( $"local/v{_version}",
                                  _buildCommit,
                                  context.Committer,
                                  releaseMessage,
                                  allowOverwrite: true );
            if( _tagInfo.TagCommits.TryGetValue( _version, out var exists ) )
            {
                Throw.DebugAssert( "We must not be able to rebuild a +deprecated commit.", !exists.IsDeprecatedVersion );
                Throw.DebugAssert( "When rebuilding an existing version, the build commit must be the same (except if the existing tag is a +fake).",
                                   exists.IsFakeVersion || _buildCommit.Sha == exists.Sha );
                // This removes any tag that are not "local/".
                if( exists.Tag.CanonicalName != t.CanonicalName )
                {
                    // Removes the other tag.
                    git.Tags.Remove( exists.Tag.CanonicalName );
                }
                exists.UpdateVersionTag( t );
            }
            else
            {
                exists = _tagInfo.AddReleaseBuildTag( _version, _buildCommit, t );
            }
            return exists;
        }
        catch( Exception ex )
        {
            // This should be a "World.Problem"
            // Problems may be future new beasts that are serializable proto/persistent-issues with a
            // "bool StillApply( ... out World.Issue issue )". 
            monitor.Error( $"""
                Unexpecting error while applying 'local/v{_version}' on commit '{_buildCommit.Sha}' with release message:
                {releaseMessage}
                """, ex );
            return null;
        }
    }

    /// <summary>
    /// Gets "Repo/version".
    /// </summary>
    /// <returns>The "Repo/version" that identifies this build info.</returns>
    public override string ToString() => _toString;
}

