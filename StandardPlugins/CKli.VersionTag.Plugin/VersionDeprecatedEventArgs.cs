using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Event raised by <see cref="VersionTagPlugin.VersionDeprecated"/> once a "ckli version deprecate" has
/// created or updated every "+deprecated" tag it implies and pushed them to the remotes: the deprecation
/// is public when a listener sees this.
/// <para>
/// A deprecation propagates. Deprecating a version deprecates every release that consumes it, transitively,
/// so a listener that mirrors the deprecation elsewhere must consider the whole <see cref="Releases"/> set
/// and not only the <see cref="Origin"/>. This is what <c>CKli.Publish.Plugin</c> does: it deprecates every
/// published profile that carries one of the <see cref="DeprecatedPackages"/>.
/// </para>
/// </summary>
public sealed class VersionDeprecatedEventArgs : WorldEventArgs
{
    readonly RepoReleaseInfo _origin;
    readonly DeprecatedTagInfo _deprecatedInfo;
    readonly ImmutableArray<RepoReleaseInfo> _releases;

    internal VersionDeprecatedEventArgs( IActivityMonitor monitor,
                                         CKliEnv context,
                                         World world,
                                         RepoReleaseInfo origin,
                                         DeprecatedTagInfo deprecatedInfo,
                                         ImmutableArray<RepoReleaseInfo> releases )
        : base( monitor, context, world )
    {
        _origin = origin;
        _deprecatedInfo = deprecatedInfo;
        _releases = releases;
    }

    /// <summary>
    /// Gets the release the command named: the root of the deprecation. It is the first
    /// of the <see cref="Releases"/>.
    /// </summary>
    public RepoReleaseInfo Origin => _origin;

    /// <summary>
    /// Gets the "+deprecated" tag information of the <see cref="Origin"/>: its
    /// <see cref="DeprecatedTagInfo.Reason"/> and the <see cref="DeprecatedTagInfo.Expiration"/> date at
    /// which its packages must be unlisted or deleted from the feeds.
    /// </summary>
    public DeprecatedTagInfo DeprecatedInfo => _deprecatedInfo;

    /// <summary>
    /// Gets whether the deprecation has expired: its packages must be unlisted or deleted from the feeds
    /// and the version tags are gone.
    /// <para>
    /// This is the <see cref="Origin"/>'s <see cref="DeprecatedTagInfo.HasExpired"/> and it governs the
    /// whole propagation: a consumer the deprecation reaches is tagged with the origin's expiration, and an
    /// already deprecated consumer only ever keeps the earlier of the two. So an expired origin means every
    /// one of the <see cref="Releases"/> has expired. "--immediate" expires the deprecation at once.
    /// </para>
    /// </summary>
    public bool HasExpired => _deprecatedInfo.HasExpired;

    /// <summary>
    /// Gets every release that is now deprecated: the <see cref="Origin"/> first, then the consumers the
    /// deprecation propagated to.
    /// <para>
    /// A release the propagation reached but could not deprecate - it has no version tag any more, or a
    /// "+fake" one - is not here: the propagation stopped there and it was only warned about.
    /// </para>
    /// </summary>
    public ImmutableArray<RepoReleaseInfo> Releases => _releases;

    /// <summary>
    /// Gets the deprecated package instances: every package the <see cref="Releases"/> produced, in the
    /// version that release published.
    /// </summary>
    public IEnumerable<PackageInstance> DeprecatedPackages => _releases.SelectMany(
                                                                r => r.Content.Produced.Select(
                                                                    p => new PackageInstance( p, r.Version ) ) );
}
