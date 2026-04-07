using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitLab API response model for a release.
/// </summary>
internal sealed class GitLabReleaseInfo
{
    [JsonPropertyName( "tag_name" )]
    public string TagName { get; set; } = "";
}
