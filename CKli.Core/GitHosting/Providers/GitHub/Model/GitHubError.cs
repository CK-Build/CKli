using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.GitHub;

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
