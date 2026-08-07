using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.Build.Plugin;

/// <summary>
/// Very basic string cache stored in "<see cref="StackRepository.StackWorkingFolder"/>/$Local" folder.
/// </summary>
public sealed class LocalStringCache
{
    readonly LocalWorldName _world;
    readonly string _name;
    string? _filePath;
    HashSet<string>? _cache;

    /// <summary>
    /// Initializes a new string cache.
    /// </summary>
    /// <param name="world">The world name.</param>
    /// <param name="name">The name of the cache.</param>
    public LocalStringCache( LocalWorldName world, string name )
    {
        _world = world;
        _name = name;
    }

    /// <summary>
    /// Gets whether this cache contains a key.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="key">The key to lookup.</param>
    /// <returns>True if this cache contains the key.</returns>
    public bool Contains( IActivityMonitor monitor, string key ) => GetCache( monitor ).Contains( key );

    /// <summary>
    /// Adds a key.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="key">The key to add.</param>
    public void Add( IActivityMonitor monitor, string key )
    {
        var c = GetCache( monitor );
        c.Add( key );
        try
        {
            Throw.DebugAssert( _filePath != null );
            File.WriteAllLines( _filePath, c );
        }
        catch( Exception ex )
        {
            monitor.Warn( $"While writing '{_filePath}'.", ex );
        }
    }

    string GetFilePath() => _filePath ??= _world.LocalDataFolder.AppendPart( $"{_name}.txt" );

    HashSet<string> GetCache( IActivityMonitor monitor )
    {
        if( _cache == null )
        {
            var path = GetFilePath();
            try
            {
                _cache = File.Exists( path )
                            ? new HashSet<string>( File.ReadLines( path ) )
                            : new HashSet<string>();
            }
            catch( Exception ex )
            {
                monitor.Warn( $"While reading '{path}'.", ex );
                _cache = new HashSet<string>();
            }
        }
        return _cache;
    }
}
