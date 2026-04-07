using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub API response model for a release.
/// </summary>
internal sealed class GitHubReleaseInfo
{
    [JsonPropertyName( "id" )]
    public long Id { get; set; }

    /// <summary>
    /// URL template for uploading assets, e.g.
    /// "https://uploads.github.com/repos/owner/repo/releases/123/assets{?name,label}".
    /// </summary>
    [JsonPropertyName( "upload_url" )]
    public string UploadUrl { get; set; } = "";
}
