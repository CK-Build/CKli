using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Gitea API request model for publishing a draft release (setting draft to false).
/// </summary>
internal sealed class GiteaPublishReleaseRequest
{
    [JsonPropertyName( "draft" )]
    public bool Draft { get; set; }
}
