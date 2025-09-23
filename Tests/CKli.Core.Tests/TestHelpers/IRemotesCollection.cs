using CK.Core;
using System;
using System.Collections.Generic;

namespace CKli.Core.Tests;

/// <summary>
/// Collection of remotes obtained by <see cref="TestEnv.UseReadOnly"/>.
/// </summary>
public interface IRemotesCollection
{
    /// <summary>
    /// Gets the name of this collection.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the "file:///" uri of the "-Stack" repository.
    /// </summary>
    Uri StackUri { get; }

    /// <summary>
    /// Gets whether pushes can be made to these repositories.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Gets the names and paths of the repositories.
    /// </summary>
    IReadOnlyDictionary<string, NormalizedPath> Repositories { get; }

    /// <summary>
    /// Gets the repository uri of a repository.
    /// Returns an invalid Uri if the <paramref name="repositoryName"/> is not in the <see cref="Repositories"/>.
    /// </summary>
    /// <param name="repositoryName">The repository name.</param>
    /// <returns>The Git Uri of the repository.</returns>
    Uri GetUriFor( string repositoryName );

    /// <summary>
    /// Returns a readable string.
    /// </summary>
    /// <returns>A readable string.</returns>
    string ToString();
}
