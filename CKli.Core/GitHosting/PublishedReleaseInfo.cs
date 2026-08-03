using CK.Core;
using System;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Models information about published release (<see cref="GitHostingProvider.CreateDraftReleaseAsync(IActivityMonitor, NormalizedPath, string, System.Threading.CancellationToken)"/>).
/// </summary>
public sealed record PublishedReleaseInfo
{
    /// <summary>
    /// Gets the release version.
    /// </summary>
    public required SVersion Version { get; init; }

    /// <summary>
    /// Gets the release identifier of this release.
    /// </summary>
    public required string ReleaseId { get; init; }

    /// <summary>
    /// Gets or sets the creation date.
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>
    /// Gets or sets whether the release has been published (immutable) or is still a mutable "draft".
    /// <para>
    /// When the hosting provider doesn't support immutable releases (draft/published), this is always false.
    /// </para>
    /// </summary>
    public bool IsPublished { get; init; }

    /// <summary>
    /// Gets the markdown description of this release.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the assets.
    /// </summary>
    public required IReadOnlyList<string> Assets { get; init; }
}
