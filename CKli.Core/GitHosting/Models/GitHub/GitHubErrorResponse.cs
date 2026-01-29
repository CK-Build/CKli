using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.GitHub;

/// <summary>
/// GitHub API error response model.
/// </summary>
internal sealed class GitHubErrorResponse
{
    [JsonPropertyName( "message" )]
    public string? Message { get; set; }

    [JsonPropertyName( "documentation_url" )]
    public string? DocumentationUrl { get; set; }

    [JsonPropertyName( "errors" )]
    public List<GitHubError>? Errors { get; set; }
}
