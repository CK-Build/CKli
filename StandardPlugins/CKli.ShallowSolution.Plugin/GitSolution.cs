using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
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
    readonly Commit _commit;

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the Branch through which this solution has been reached.
    /// <para>
    /// This is the context of the read, not necessarily what has been read: see <see cref="Commit"/>.
    /// </para>
    /// </summary>
    public Branch GitBranch => _branch;

    /// <summary>
    /// Gets the commit from which this solution has been read. This is the <see cref="GitBranch"/>'s tip
    /// unless the solution has been read from an explicit commit of that branch's history: this is how a
    /// branch that doesn't exist yet is read from the commit it would be created at
    /// (see <c>HotBranch.GetStartCommit</c>).
    /// </summary>
    public Commit Commit => _commit;

    GitSolution( Repo repo, Branch branch, Commit commit )
    {
        _repo = repo;
        _branch = branch;
        _commit = commit;
    }

    internal static GitSolution? Create( IActivityMonitor monitor, Repo repo, Branch branch, Commit commit, INormalizedFileProvider files, XDocument doc )
    {
        var s = new GitSolution( repo, branch, commit );
        return s.Initialize( monitor, files, doc ) ? s : null;
    }

    /// <summary>
    /// Overridden to return the repository display name and branch, with the commit when it is not the
    /// branch's tip.
    /// </summary>
    /// <returns>Repository display name and branch.</returns>
    public override string ToString() => _commit == _branch.Tip
                                            ? $"{_repo.DisplayPath} ({_branch.FriendlyName})"
                                            : $"{_repo.DisplayPath} ({_branch.FriendlyName} at {_commit.Sha.AsSpan( 0, 7 )})";
}

