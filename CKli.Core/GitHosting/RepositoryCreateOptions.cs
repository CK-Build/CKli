namespace CKli.Core.GitHosting;

/// <summary>
/// Options for creating a new repository.
/// </summary>
public sealed class RepositoryCreateOptions
{
    /// <summary>
    /// Generates a default repository description.
    /// </summary>
    /// <param name="repoName">The repository name.</param>
    /// <param name="stackName">The stack name, if available.</param>
    /// <returns>A default description string.</returns>
    public static string GenerateDefaultDescription( string repoName, string? stackName = null )
    {
        return stackName != null
            ? $"{repoName} ({stackName} stack)"
            : $"{repoName} repository";
    }

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
    /// Gets or sets whether the repository should be private.
    /// Defaults to false (public).
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// Gets or sets whether to initialize the repository with a README.
    /// Defaults to false.
    /// </summary>
    public bool AutoInit { get; init; }

    /// <summary>
    /// Gets or sets the default branch name.
    /// If null, the provider's default is used (usually "main" or "master").
    /// </summary>
    public string? DefaultBranch { get; init; }

    /// <summary>
    /// Gets or sets the .gitignore template to use.
    /// Provider-specific (e.g., "VisualStudio", "Node", "Python").
    /// </summary>
    public string? GitIgnoreTemplate { get; init; }

    /// <summary>
    /// Gets or sets the license template to use.
    /// Provider-specific (e.g., "MIT", "Apache-2.0", "GPL-3.0").
    /// </summary>
    public string? LicenseTemplate { get; init; }
}
