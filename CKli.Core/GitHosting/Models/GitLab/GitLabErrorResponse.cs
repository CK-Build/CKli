using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.GitLab;

/// <summary>
/// GitLab API error response model.
/// </summary>
internal sealed class GitLabErrorResponse
{
    [JsonPropertyName( "message" )]
    public object? Message { get; set; }

    [JsonPropertyName( "error" )]
    public string? Error { get; set; }

    [JsonPropertyName( "error_description" )]
    public string? ErrorDescription { get; set; }
}
