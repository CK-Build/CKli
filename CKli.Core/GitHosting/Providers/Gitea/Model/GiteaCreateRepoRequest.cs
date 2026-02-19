using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

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
