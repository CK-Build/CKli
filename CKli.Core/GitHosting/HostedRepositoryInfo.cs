using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// Represents information about a repository.
/// </summary>
public sealed record HostedRepositoryInfo
{
    /// <summary>
    /// Gets or sets the repository path in the hosting provider.
    /// This is empty if the repository doesn't exist.
    /// <para>
    /// The repository name is the <see cref="NormalizedPath.LastPart"/>.
    /// </para>
    /// </summary>
    public required NormalizedPath RepoPath { get; init; }

    /// <summary>
    /// Gets whether this repository exists.
    /// </summary>
    public bool Exists => !RepoPath.IsEmptyPath;

    /// <summary>
    /// Gets or sets whether the repository is private.
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// Gets or sets the repository description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets whether the repository is archived.
    /// </summary>
    public bool IsArchived { get; init; }

    /// <summary>
    /// Gets or sets the clone url.
    /// </summary>
    public string? CloneUrl { get; init; }

    /// <summary>
    /// Gets or sets the web URL for viewing the repository.
    /// </summary>
    public string? WebUrl { get; init; }

    /// <summary>
    /// Gets or sets the creation date.
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>
    /// Gets or sets the last update date.
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}
