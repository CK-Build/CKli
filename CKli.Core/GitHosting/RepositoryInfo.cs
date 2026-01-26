using System;

namespace CKli.Core.GitHosting;

/// <summary>
/// Represents information about a repository.
/// </summary>
public sealed record RepositoryInfo
{
    /// <summary>
    /// Gets or sets the repository owner (organization or user).
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// Gets or sets the repository name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets the repository description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets whether the repository is private.
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// Gets or sets whether the repository is archived.
    /// </summary>
    public bool IsArchived { get; init; }

    /// <summary>
    /// Gets or sets whether the repository is empty (has no commits).
    /// </summary>
    public bool IsEmpty { get; init; }

    /// <summary>
    /// Gets or sets the default branch name.
    /// </summary>
    public string? DefaultBranch { get; init; }

    /// <summary>
    /// Gets or sets the HTTPS clone URL.
    /// </summary>
    public string? CloneUrlHttps { get; init; }

    /// <summary>
    /// Gets or sets the SSH clone URL.
    /// </summary>
    public string? CloneUrlSsh { get; init; }

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
