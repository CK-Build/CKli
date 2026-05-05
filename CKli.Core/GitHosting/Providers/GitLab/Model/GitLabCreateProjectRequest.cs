using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

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

    [JsonPropertyName( "default_branch" )]
    public string? DefaultBranch { get; set; }

    /// <summary>
    /// Always true to be able to specify the default branch.
    /// See https://docs.gitlab.com/api/projects/#create-a-project.
    /// </summary>
    [JsonPropertyName( "initialize_with_readme" )]
    public bool InitializeWithReadme { get; } = true;
}

