using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitLab API request model for creating a release asset link.
/// </summary>
internal sealed class GitLabAssetLinkRequest
{
    [JsonPropertyName( "name" )]
    public required string Name { get; set; }

    [JsonPropertyName( "url" )]
    public required string Url { get; set; }

    [JsonPropertyName( "link_type" )]
    public string LinkType { get; set; } = "package";
}
