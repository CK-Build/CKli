using System.Text.Json.Serialization;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// GitHub API request model for creating a release.
/// </summary>
internal sealed class GitHubCreateReleaseRequest
{
    [JsonPropertyName( "tag_name" )]
    public required string TagName { get; set; }

    /// <summary>
    /// The commitish the tag is created from. Documented as unused when the tag already exists (which is the
    /// <see cref="GitHostingProvider.CreateDraftReleaseAsync"/> contract) but GitHub resolves it in all cases:
    /// left unset, it falls back to the repository default branch and a repository whose default branch doesn't
    /// exist (CKli pushes 'stable', GitHub keeps the account's 'main') gets a 422 "Invalid target_commitish
    /// parameter" even though the tag is there. Setting it to the tag makes it always resolvable.
    /// </summary>
    [JsonPropertyName( "target_commitish" )]
    public required string TargetCommitish { get; set; }

    [JsonPropertyName( "name" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Name { get; set; }

    [JsonPropertyName( "draft" )]
    public bool Draft { get; set; }

    [JsonPropertyName( "prerelease" )]
    public bool Prerelease { get; set; }

    [JsonPropertyName( "generate_release_notes" )]
    public bool GenerateReleaseNotes { get; set; }
}
