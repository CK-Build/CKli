using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitLab API request model for updating a project (default branch).
/// </summary>
internal sealed class GitLabUpdateDefaultBranchRequest
{
    [JsonPropertyName( "default_branch" )]
    public required string DefaultBranch { get; set; }
}
