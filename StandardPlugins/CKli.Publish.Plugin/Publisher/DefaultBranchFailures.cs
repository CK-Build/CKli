using CK.Core;
using System.Collections.Generic;
using System.Threading;

namespace CKli.Publish.Plugin;

/// <summary>
/// Collects the repositories whose remote default branch could not be made the root branch during a publication,
/// so that a single warning reports them all once the publication is over.
/// <para>
/// This is not a publication failure: hosts require more than push rights to change a repository's default branch
/// (GitHub requires administration rights on the repository), and a token that can publish but not administer is
/// legitimate. The hosts' answers are logged at the <see cref="LogLevel.Trace"/> level.
/// </para>
/// <para>
/// This is thread safe: the direct publications run concurrently.
/// </para>
/// </summary>
sealed class DefaultBranchFailures
{
    readonly Lock _lock = new();
    readonly List<string> _repositories = new();
    string? _branchName;

    /// <summary>
    /// Records that <paramref name="branchName"/> could not be made the default branch of a repository.
    /// </summary>
    /// <param name="repoDisplayPath">The repository's display path.</param>
    /// <param name="branchName">The root branch name.</param>
    public void Add( string repoDisplayPath, string branchName )
    {
        lock( _lock )
        {
            _repositories.Add( repoDisplayPath );
            _branchName = branchName;
        }
    }

    /// <summary>
    /// Emits a single warning that lists the collected repositories (if any) and clears them.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    public void Warn( IActivityMonitor monitor )
    {
        lock( _lock )
        {
            if( _repositories.Count == 0 ) return;
            monitor.Warn( $"""
                Unable to make '{_branchName}' the default branch of the remote of {(_repositories.Count == 1 ? "1 repository" : $"{_repositories.Count} repositories")}: '{_repositories.Concatenate( "', '" )}'.
                Changing it requires administration rights on the remote repository (the "Admin" role on GitHub). The publication is not affected.
                """ );
            _repositories.Clear();
        }
    }
}
