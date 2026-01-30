using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Models.GitHub;

/// <summary>
/// GitHub API model for repository owner.
/// </summary>
internal sealed class GitHubOwner
{
    [JsonPropertyName( "login" )]
    public string Login { get; set; } = "";

    [JsonPropertyName( "type" )]
    public string Type { get; set; } = "";
}
