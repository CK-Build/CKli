using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Encapsulates the arguments for CKli.Plugins helper.
/// </summary>
public sealed class PluginCollectorContext
{
    readonly WorldName _worldName;
    readonly IReadOnlyDictionary<XName, (XElement Config, bool IsDisabled)> _pluginsConfiguration;
    byte[]? _signature;

    /// <summary>
    /// Initializes a new <see cref="PluginCollectorContext"/>.
    /// </summary>
    /// <param name="worldName">The world name.</param>
    /// <param name="pluginsConfiguration">The plugins configuration.</param>
    public PluginCollectorContext( WorldName worldName,
                                   IReadOnlyDictionary<XName, (XElement Config, bool IsDisabled)> pluginsConfiguration )
    {
        _worldName = worldName;
        _pluginsConfiguration = pluginsConfiguration;
    }

    /// <summary>
    /// Gets the world name.
    /// </summary>
    public WorldName WorldName => _worldName;

    /// <summary>
    /// Gets the plugin configurations.
    /// </summary>
    public IReadOnlyDictionary<XName, (XElement Config, bool IsDisabled)> PluginsConfiguration => _pluginsConfiguration;

    /// <summary>
    /// Gets the plugin configuration signature.
    /// </summary>
    public ReadOnlySpan<byte> Signature => new ReadOnlySpan<byte>( _signature ??= ComputeSignature( _pluginsConfiguration ) );

    static byte[] ComputeSignature( IReadOnlyDictionary<XName, (XElement Config, bool IsDisabled)> configs )
    {
        using var hasher = IncrementalHash.CreateHash( HashAlgorithmName.SHA1 );
        foreach( var (name, isDisabled) in configs.Select( kv => (kv.Key,kv.Value.IsDisabled) ).OrderBy( kv => kv.Key ) )
        {
            hasher.Append( name.LocalName );
            hasher.Append( isDisabled );
        }
        return hasher.GetHashAndReset();
    }

}

