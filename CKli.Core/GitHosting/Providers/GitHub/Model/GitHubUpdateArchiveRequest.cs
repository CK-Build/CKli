using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub API request model for updating a repository (archive).
/// </summary>
internal class GitHubUpdateArchiveRequest
{
    /// <summary>
    /// The repository name. Documented as optional but GitHub answers a 422 "Validation Failed" with an
    /// empty "errors" array when the body carries only <see cref="Archived"/>.
    /// </summary>
    [JsonPropertyName( "name" )]
    public required string Name { get; set; }

    [JsonPropertyName( "archived" )]
    public bool Archived { get; set; }
}
