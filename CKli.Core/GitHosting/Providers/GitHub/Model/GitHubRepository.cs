using CK.Core;
using System;
using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.GitHub;

/// <summary>
/// GitHub API response model for a repository.
/// </summary>
internal sealed class GitHubRepository
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

    [JsonPropertyName( "default_branch" )]
    public string? DefaultBranch { get; set; }

    [JsonPropertyName( "clone_url" )]
    public string? CloneUrl { get; set; }

    [JsonPropertyName( "ssh_url" )]
    public string? SshUrl { get; set; }

    [JsonPropertyName( "html_url" )]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName( "created_at" )]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName( "updated_at" )]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName( "owner" )]
    public GitHubOwner? Owner { get; set; }
}

