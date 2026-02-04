using System;
using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;


/// <summary>
/// GitLab API response model for a project (repository).
/// </summary>
internal sealed class GitLabProject
{
    [JsonPropertyName( "id" )]
    public long Id { get; set; }

    [JsonPropertyName( "name" )]
    public string Name { get; set; } = "";

    [JsonPropertyName( "path" )]
    public string Path { get; set; } = "";

    [JsonPropertyName( "path_with_namespace" )]
    public string PathWithNamespace { get; set; } = "";

    [JsonPropertyName( "description" )]
    public string? Description { get; set; }

    [JsonPropertyName( "visibility" )]
    public string Visibility { get; set; } = "";

    [JsonPropertyName( "archived" )]
    public bool Archived { get; set; }

    [JsonPropertyName( "empty_repo" )]
    public bool EmptyRepo { get; set; }

    [JsonPropertyName( "default_branch" )]
    public string? DefaultBranch { get; set; }

    [JsonPropertyName( "http_url_to_repo" )]
    public string? HttpUrlToRepo { get; set; }

    [JsonPropertyName( "ssh_url_to_repo" )]
    public string? SshUrlToRepo { get; set; }

    [JsonPropertyName( "web_url" )]
    public string? WebUrl { get; set; }

    [JsonPropertyName( "created_at" )]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName( "last_activity_at" )]
    public DateTime? LastActivityAt { get; set; }

    [JsonPropertyName( "namespace" )]
    public GitLabNamespace? Namespace { get; set; }
}

