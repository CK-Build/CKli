using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Gitea API request model for updating a repository (default branch).
/// </summary>
internal sealed class GiteaUpdateDefaultBranchRequest
{
    [JsonPropertyName( "default_branch" )]
    public required string DefaultBranch { get; set; }
}
