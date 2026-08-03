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

    /// <summary>
    /// Creates a new <see cref="PublishedReleaseInfo"/> or null and emits a warning
    /// if the <paramref name="tag"/> is not a <see cref="SVersion"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="tag">The release tag to parse that must be a valid SVersion.</param>
    /// <param name="isPublished"><see cref="IsPublished"/>.</param>
    /// <param name="createdAt"><see cref="CreatedAt"/>.</param>
    /// <param name="description"><see cref="Description"/>.</param>
    /// <param name="releaseId"><see cref="ReleaseId"/>.</param>
    /// <param name="assets"><see cref="Assets"/>.</param>
    /// <returns>The released info or null.</returns>
    public static PublishedReleaseInfo? Create( IActivityMonitor monitor,
                                                string tag,
                                                bool isPublished,
                                                DateTime? createdAt,
                                                string description,
                                                string releaseId,
                                                List<string> assets )
    {
        PublishedReleaseInfo? info = null;
        var v = SVersion.ParseNoThrow( tag );
        if( !v.IsValid )
        {
            monitor.Warn( $"Ignoring release with tag '{tag}' that is not a Semantic Version (error: '{v.ErrorMessage}')." );
        }
        else
        {
            info = new PublishedReleaseInfo
            {
                Version = v,
                ReleaseId = releaseId,
                CreatedAt = createdAt,
                IsPublished = isPublished,
                Description = description,
                Assets = assets
            };
        }

        return info;
    }
}
