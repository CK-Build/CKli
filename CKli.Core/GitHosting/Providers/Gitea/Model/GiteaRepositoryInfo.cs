using System;
using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

internal sealed class GiteaRepositoryInfo
{
    [JsonPropertyName( "id" )]
    public long Id { get; set; }

    [JsonPropertyName( "name" )]
    public string Name { get; set; } = "";

    [JsonPropertyName( "full_name" )]
    public string FullName { get; set; } = "";

    [JsonPropertyName( "description" )]
    public string? Description { get; set; }

    [JsonPropertyName( "private" )]
    public bool Private { get; set; }

    [JsonPropertyName( "archived" )]
    public bool Archived { get; set; }

    [JsonPropertyName( "clone_url" )]
    public string? CloneUrl { get; set; }

    [JsonPropertyName( "html_url" )]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName( "created_at" )]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName( "updated_at" )]
    public DateTime? UpdatedAt { get; set; }
}
