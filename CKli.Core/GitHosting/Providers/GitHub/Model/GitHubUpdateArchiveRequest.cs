using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub API request model for updating a repository (archive).
/// </summary>
internal class GitHubUpdateArchiveRequest
{
    [JsonPropertyName( "archived" )]
    public bool Archived { get; set; }


}
