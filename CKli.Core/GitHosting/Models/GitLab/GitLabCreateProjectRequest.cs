using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.GitLab;

/// <summary>
/// GitLab API request model for creating a project.
/// </summary>
internal sealed class GitLabCreateProjectRequest
{
    [JsonPropertyName( "name" )]
    public required string Name { get; set; }

    [JsonPropertyName( "path" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Path { get; set; }

    [JsonPropertyName( "description" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Description { get; set; }

    [JsonPropertyName( "visibility" )]
    public string Visibility { get; set; } = "private";

    [JsonPropertyName( "namespace_id" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public long? NamespaceId { get; set; }

    [JsonPropertyName( "initialize_with_readme" )]
    public bool InitializeWithReadme { get; set; }
}
