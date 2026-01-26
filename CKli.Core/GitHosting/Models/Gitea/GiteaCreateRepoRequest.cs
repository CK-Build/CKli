using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.Gitea;

/// <summary>
/// Gitea API request model for creating a repository.
/// </summary>
internal sealed class GiteaCreateRepoRequest
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

    [JsonPropertyName( "gitignores" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Gitignores { get; set; }

    [JsonPropertyName( "license" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? License { get; set; }
}

/// <summary>
/// Gitea API request model for updating a repository (archive).
/// </summary>
internal sealed class GiteaUpdateRepoRequest
{
    [JsonPropertyName( "archived" )]
    public bool Archived { get; set; }
}
