using CK.Core;
using CKli.Core;
using System;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Internal key for a Repo/Version.
/// </summary>
/// <param name="Repo">The repository.</param>
/// <param name="Version">The version.</param>
readonly record struct RepoKey( Repo Repo, SVersion Version )
{
    readonly int _hash = HashCode.Combine( Repo, Version );

    public override int GetHashCode() => _hash;

    public override string ToString() => ToString( Repo, Version );

    public static string ToString( Repo r, SVersion v ) => $"{r.DisplayPath}/v{v}";
}
