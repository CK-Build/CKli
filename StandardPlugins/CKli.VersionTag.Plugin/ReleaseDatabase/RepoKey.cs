using CK.Core;
using CKli.Core;
using System;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Internal key for a Repo/Version.
/// </summary>
/// <param name="TagCommit">The TagCommit.</param>
/// <param name="Version">The version. Either <see cref="TagCommit.Version"/> or <see cref="TagCommit.CI0Version"/>.</param>
readonly record struct RepoKey( TagCommit TagCommit, SVersion Version )
{
    readonly int _hash = HashCode.Combine( TagCommit.Repo, Version );

    public Repo Repo => TagCommit.Repo;

    public override int GetHashCode() => _hash;

    public override string ToString() => ToString( Repo, Version );

    public static string ToString( Repo r, SVersion v ) => $"{r.DisplayPath}/{v.ParsedPrefix}v{v}";
}
