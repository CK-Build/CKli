using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub API request model for publishing a draft release (setting draft to false).
/// </summary>
internal sealed class GitHubPublishReleaseRequest
{
    [JsonPropertyName( "draft" )]
    public bool Draft { get; set; }
}
