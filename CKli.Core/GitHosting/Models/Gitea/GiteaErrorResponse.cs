using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.Gitea;

/// <summary>
/// Gitea API error response model.
/// </summary>
internal sealed class GiteaErrorResponse
{
    [JsonPropertyName( "message" )]
    public string? Message { get; set; }

    [JsonPropertyName( "url" )]
    public string? Url { get; set; }
}
