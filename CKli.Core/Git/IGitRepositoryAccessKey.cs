using CK.Core;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

/// <summary>
/// Captures access PAT names associated to a repository or a set of repositories: this
/// is potentially independent of a specific repository.
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
    /// Gets the access protection kind.
    /// <list type="bullet">
    ///     <item>
    ///         True means that the repository can be read without the secret (obtained from the store and <see cref="ReadPATKeyName"/>).
    ///         No authentication is required.
    ///     </item>
    ///     <item>
    ///         False means that the repository requires the <see cref="ReadPATKeyName"/> secret.
    ///     </item>
    ///     <item>
    ///         When null, the notion of public/private is meaningless. A secret is always required (for
    ///         coherency, <see cref="GetWriteCredentials(IActivityMonitor, out UsernamePasswordCredentials?)"/> always outputs
    ///         a non null credentials with a non null <see cref="UsernamePasswordCredentials.Password"/>) but its value is not used.
    ///         <para>
    ///         <see cref="ReadPATKeyName"/> and <see cref="WritePATKeyName"/> are both set to <see cref="PrefixPAT"/>.
    ///         </para>
    ///     </item>
    /// </list>
    /// A <c>null</c> IsPublic should be considered <c>true</c> (accessing this kind of repositories has
    /// no restriction) but the distinction matters.
    /// </summary>
    bool? IsPublic { get;}

    /// <summary>
    /// Common PAT prefix.
    /// <para>
    /// This is the fundamental key. The <see cref="ReadPATKeyName"/> and <see cref="WritePATKeyName"/> are derived from
    /// this value and the <see cref="GitHostingProvider"/> that manages this repository is identified by this prefix
    /// and IsPublic.
    /// </para>
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
    /// This is always successful and <paramref name="creds"/> is null when <see cref="IsPublic"/> is null or true.
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
    /// Gets the hosting provider that can be used to manage hosting of repositories that share this key.
    /// <para>
    /// When no known provider can be associated to a key, this is null (repositories can only be handled via Git).
    /// </para>
    /// </summary>
    GitHostingProvider? HostingProvider { get; }

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
    /// Returns "<see cref="PrefixPAT"/> (<see cref="IsPublic"/>)" information.
    /// This is used as a key to identify a <see cref="GitHostingProvider"/>.
    /// </summary>
    /// <returns>The PAT prefix and whether this key is public.</returns>
    string ToString();
}
