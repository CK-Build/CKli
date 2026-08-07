using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Minimal abstraction of a versioned tagged commit in a repository.
/// See <see cref="ITagCommitProvider"/>.
/// </summary>
public interface ITagCommit
{
    /// <summary>
    /// Gets the repository.
    /// </summary>
    Repo Repo { get; }

    /// <summary>
    /// Gets the version.
    /// </summary>
    SVersion Version { get; }

    /// <summary>
    /// Gets the commit.
    /// </summary>
    Commit Commit { get; }


    /// <summary>
    /// Creates a minimal <see cref="ITagCommit"/> instance.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="version">The version.</param>
    /// <param name="commit">The commit.</param>
    /// <returns></returns>
    public static ITagCommit Create( Repo repo, SVersion version, Commit commit )
    {
        return new TagCommit( repo, version, commit );
    }

    internal sealed class TagCommit : ITagCommit
    {
        public TagCommit( Repo repo, SVersion version, Commit commit )
        {
            Repo = repo;
            Version = version;
            Commit = commit;
        }

        public Repo Repo { get; }
        public SVersion Version { get; }
        public Commit Commit { get; }
        public override string ToString() => $"Tag '{Repo.DisplayPath}/{Version.ParsedText}' references Commit '{Commit.Sha}'";
    }
}
