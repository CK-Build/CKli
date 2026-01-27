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

/// <summary>
/// GitHub API error detail.
/// </summary>
internal sealed class GitHubError
{
    [JsonPropertyName( "resource" )]
    public string? Resource { get; set; }

    [JsonPropertyName( "code" )]
    public string? Code { get; set; }

    [JsonPropertyName( "field" )]
    public string? Field { get; set; }

    [JsonPropertyName( "message" )]
    public string? Message { get; set; }
}
