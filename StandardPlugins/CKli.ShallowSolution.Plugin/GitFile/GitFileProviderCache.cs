using CK.Core;
using LibGit2Sharp;
using System.Collections.Generic;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Cache for <see cref="INormalizedFileProvider"/>.
/// It is optional for <see cref="INormalizedFileProvider.GetFiles(Commit, bool, GitFileProviderCache?)"/> and can be
/// used independently.
/// </summary>
public sealed class GitFileProviderCache
{
    readonly Dictionary<string, INormalizedFileProvider> _cache;

    /// <summary>
    /// Initializes a new empty cache.
    /// </summary>
    public GitFileProviderCache()
    {
        // Use Ordinal equality.
        _cache = new Dictionary<string, INormalizedFileProvider>();    
    }

    /// <summary>
    /// Finds or creates the file provider for a folder on the file system.
    /// </summary>
    /// <param name="root">The root folder to consider.</param>
    /// <returns>The file provider.</returns>
    public INormalizedFileProvider GetFiles( NormalizedPath root )
    {
        if( !_cache.TryGetValue( root.Path, out var content ) )
        {
            content = new CheckedOutFileProvider( root );
            _cache.Add( root.Path, content );
        }
        return content;
    }

    /// <summary>
    /// Finds or creates the file provider for a commit.
    /// </summary>
    /// <param name="commit">The commit that must be inspected.</param>
    /// <returns>The file provider.</returns>
    public INormalizedFileProvider GetFiles( Commit commit ) => GetFiles( commit.Tree );

    /// <summary>
    /// Finds or creates the file provider for a git Tree.
    /// </summary>
    /// <param name="tree">The tree that must be inspected.</param>
    /// <returns>The file provider.</returns>
    public INormalizedFileProvider GetFiles( Tree tree )
    {
        if( !_cache.TryGetValue( tree.Sha, out var content ) )
        {
            content = new TreeFolder( tree );
            _cache.Add( tree.Sha, content );
        }
        return content;
    }

}
