using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitLab API model for project namespace (group or user).
/// </summary>
internal sealed class GitLabNamespace
{
    [JsonPropertyName( "id" )]
    public long Id { get; set; }

    [JsonPropertyName( "name" )]
    public string Name { get; set; } = "";

    [JsonPropertyName( "path" )]
    public string Path { get; set; } = "";

    [JsonPropertyName( "full_path" )]
    public string FullPath { get; set; } = "";

    [JsonPropertyName( "kind" )]
    public string Kind { get; set; } = "";
}

