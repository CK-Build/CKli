using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitLab API request model for creating a release.
/// </summary>
internal sealed class GitLabCreateReleaseRequest
{
    [JsonPropertyName( "tag_name" )]
    public required string TagName { get; set; }

    [JsonPropertyName( "name" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Name { get; set; }

    [JsonPropertyName( "description" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Description { get; set; }
}
