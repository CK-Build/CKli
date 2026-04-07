using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Gitea API response model for a release.
/// </summary>
internal sealed class GiteaReleaseInfo
{
    [JsonPropertyName( "id" )]
    public long Id { get; set; }
}
