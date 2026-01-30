using CK.Core;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

/// <summary>
/// Captures access PAT names associated to a repository or a set of repositories: this
/// is potentially independent of a specific repository. This is the key that identifies a <see cref="GitHostingProvider"/>:
/// the <see cref="ToString()"/> is used as the key.
/// <para>
/// When <see cref="IsPublic"/> is true, the <see cref="GetReadCredentials(IActivityMonitor, out UsernamePasswordCredentials?)"/>
/// always succeeds and outputs a null credentials.
/// </para>
/// <para>
/// When used for a set of repositories (as <see cref="GitHostingProvider"/> do), handling heterogeneous private/public
/// set of repositories can be done thanks to <see cref="ToPrivateAccessKey"/> and <see cref="ToPublicAccessKey"/>.
/// </para>
/// </summary>
public interface IGitRepositoryAccessKey
{
    /// <summary>
    /// Gets whether the Git repository can be read without the <see cref="ReadPATKeyName"/>.
    /// </summary>
    bool IsPublic { get;}

    /// <summary>
    /// Gets the known Git cloud provider.
    /// </summary>
    KnownCloudGitProvider KnownGitProvider { get; }

    /// <summary>
    /// Common PAT prefix.
    /// </summary>
    string PrefixPAT { get; }

    /// <summary>
    /// Gets the basic PAT key name to read/clone this repository.
    /// This PAT is required only if <see cref="IsPublic"/> is false.
    /// </summary>
    string ReadPATKeyName { get; }

    /// <summary>
    /// Gets the write PAT key name for this repository.
    /// This PAT must allow pushes to the repository.
    /// </summary>
    string WritePATKeyName { get; }

    /// <summary>
    /// Tries to get the credentials to be able to read the remote repository.
    /// This is always successful and <paramref name="creds"/> is null when <see cref="IsPublic"/> is true.
    /// <para>
    /// The <see cref="UsernamePasswordCredentials.Username"/> is always "CKli" (when <see cref="IsPublic"/> is false).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="creds">The credentials to use or null for public repository.</param>
    /// <returns>True on success, false on error.</returns>
    bool GetReadCredentials( IActivityMonitor monitor, out UsernamePasswordCredentials? creds );

    /// <summary>
    /// Tries to get the credentials to be able to push to the remote repository.
    /// <para>
    /// The <see cref="UsernamePasswordCredentials.Username"/> is always "CKli".
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="creds">The credentials to use.</param>
    /// <returns>True on success, false on error.</returns>
    bool GetWriteCredentials( IActivityMonitor monitor, [NotNullWhen( true )] out UsernamePasswordCredentials? creds );

    /// <summary>
    /// Gets an access key with a true <see cref="IsPublic"/> or this one if it is already public.
    /// </summary>
    /// <returns>The associated private access key.</returns>
    IGitRepositoryAccessKey ToPublicAccessKey();

    /// <summary>
    /// Gets an access key with a false <see cref="IsPublic"/> or this one if it is already private.
    /// </summary>
    /// <returns>The associated public access key.</returns>
    IGitRepositoryAccessKey ToPrivateAccessKey();

    /// <summary>
    /// Returns "<see cref="PrefixPAT"/>(<see cref="IsPublic"/>)" information.
    /// This is used as a key to identify a <see cref="GitHostingProvider"/>.
    /// </summary>
    /// <returns>The PAT prefix and whether this key is public.</returns>
    string ToString();
}
