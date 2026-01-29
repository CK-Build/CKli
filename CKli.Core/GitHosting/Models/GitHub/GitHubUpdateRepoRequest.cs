using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.GitHub;

/// <summary>
/// GitHub API request model for updating a repository (archive).
/// </summary>
internal sealed class GitHubUpdateRepoRequest
{
    [JsonPropertyName( "archived" )]
    public bool Archived { get; set; }
}
