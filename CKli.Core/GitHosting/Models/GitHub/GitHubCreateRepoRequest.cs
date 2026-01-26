using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.GitHub;

/// <summary>
/// GitHub API request model for creating a repository.
/// </summary>
internal sealed class GitHubCreateRepoRequest
{
    [JsonPropertyName( "name" )]
    public required string Name { get; set; }

    [JsonPropertyName( "description" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Description { get; set; }

    [JsonPropertyName( "private" )]
    public bool Private { get; set; }

    [JsonPropertyName( "auto_init" )]
    public bool AutoInit { get; set; }

    [JsonPropertyName( "gitignore_template" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? GitignoreTemplate { get; set; }

    [JsonPropertyName( "license_template" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? LicenseTemplate { get; set; }
}

/// <summary>
/// GitHub API request model for updating a repository (archive).
/// </summary>
internal sealed class GitHubUpdateRepoRequest
{
    [JsonPropertyName( "archived" )]
    public bool Archived { get; set; }
}
