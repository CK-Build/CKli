namespace CKli.Core;

/// <summary>
/// Type of the <see cref="IGitHostingProvider"/> that CKli knows and can handle.
/// </summary>
public enum GitHostingType
{
    /// <summary>
    /// Non applicable.
    /// </summary>
    None,

    /// <summary>
    /// Supports https://github.com and GitHub Enterprise instances.
    /// </summary>
    GitHub,

    /// <summary>
    /// Supports https://gitlab.com and self-hosted GitLab instances.
    /// </summary>
    GitLab,

    /// <summary>
    /// Gitea is self-hosted only - there is no official cloud instance.
    /// </summary>
    Gitea,

    /// <summary>
    /// Local file system provider. Repositories are created as bare repositories.
    /// <para>
    /// The <see cref="IGitHostingProvider.HostName"/> is a root folder path into which
    /// repositories are managed.
    /// </para>
    /// <para>
    /// The <see cref="IGitHostingProvider.BaseApiUrl"/> is the "file://" url to the folder.
    /// </para>
    /// </summary>
    FileSystem
}
