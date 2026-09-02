using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub API request model for updating a repository (default branch).
/// </summary>
internal sealed class GitHubUpdateDefaultBranchRequest
{
    /// <summary>
    /// The repository name. Documented as optional but GitHub answers a 422 "Validation Failed" with an
    /// empty "errors" array when the body carries only <see cref="DefaultBranch"/>.
    /// </summary>
    [JsonPropertyName( "name" )]
    public required string Name { get; set; }

    [JsonPropertyName( "default_branch" )]
    public required string DefaultBranch { get; set; }
}
