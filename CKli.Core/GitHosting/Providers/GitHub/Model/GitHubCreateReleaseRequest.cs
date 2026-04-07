using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub API request model for creating a release.
/// </summary>
internal sealed class GitHubCreateReleaseRequest
{
    [JsonPropertyName( "tag_name" )]
    public required string TagName { get; set; }

    [JsonPropertyName( "name" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Name { get; set; }

    [JsonPropertyName( "draft" )]
    public bool Draft { get; set; }

    [JsonPropertyName( "prerelease" )]
    public bool Prerelease { get; set; }

    [JsonPropertyName( "generate_release_notes" )]
    public bool GenerateReleaseNotes { get; set; }
}
