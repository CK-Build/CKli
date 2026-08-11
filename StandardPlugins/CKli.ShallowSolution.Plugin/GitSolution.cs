using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Diagnostics;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Read only model of a solution as a consumer/producer of packages.
/// <para>
/// This can only obtained for a <see cref="GitBranch"/> in a <see cref="Repo"/> and only
/// exposes the <see cref="GitSolutionContent.Projects"/> and the <see cref="GitSolutionContent.Consumed"/> packages.
/// </para>
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class GitSolution : GitSolutionContent
{
    readonly Repo _repo;
    readonly Branch _branch;

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the Branch from which this solution has been read.
    /// </summary>
    public Branch GitBranch => _branch;


    GitSolution( Repo repo, Branch branch )
    {
        _repo = repo;
        _branch = branch;
    }

    internal static GitSolution? Create( IActivityMonitor monitor, Repo repo, Branch branch, INormalizedFileProvider files, XDocument doc )
    {
        var s = new GitSolution( repo, branch );
        return s.Initialize( monitor, files, doc ) ? s : null;
    }

    /// <summary>
    /// Overridden to return the repository display name and branch.
    /// </summary>
    /// <returns>Repository display name and branch.</returns>
    public override string ToString() => $"{_repo.DisplayPath} ({_branch.FriendlyName})";
}

