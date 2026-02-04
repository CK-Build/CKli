using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Gitea API request model for updating a repository (archive).
/// </summary>
internal class GiteaUpdateArchiveRequest
{
    [JsonPropertyName( "archived" )]
    public bool Archived { get; set; }


}
