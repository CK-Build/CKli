using CSemVer;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

/// <summary>
/// Plugin description.
/// </summary>
public sealed class PluginInfo
{
    readonly string _fullPluginName;
    readonly string _pluginName;
    readonly IReadOnlyList<IPluginTypeInfo> _pluginTypes;
    readonly string? _originalVersion;
    readonly PluginStatus _status;
    InformationalVersion? _informationalVersion;

    /// <summary>
    /// Initializes a new PluginInfo.
    /// </summary>
    /// <param name="fullPluginName">The full "CKli.XXX.Plugin" name.</param>
    /// <param name="pluginName">The plugin name.</param>
    /// <param name="status">The status.</param>
    /// <param name="informationalVersion">The informational version (for a packaged plugin), null for a source plugin.</param>
    /// <param name="pluginTypes">The types defined by this plugin.</param>
    public PluginInfo( string fullPluginName,
                       string pluginName,
                       PluginStatus status,
                       string? informationalVersion,
                       IReadOnlyList<IPluginTypeInfo> pluginTypes )
    {
        _fullPluginName = fullPluginName;
        _pluginName = pluginName;
        _status = status;
        _originalVersion = informationalVersion;
        _pluginTypes = pluginTypes;
    }

    /// <summary>
    /// Gets the full plugin name "CKli.XXX.Plugin" that is the name of the project/package.
    /// </summary>
    public string FullPluginName => _fullPluginName;

    /// <summary>
    /// Gets the short plugin name.
    /// </summary>
    public string PluginName => _pluginName;

    /// <summary>
    /// Gets the status flags.
    /// </summary>
    public PluginStatus Status => _status;

    /// <summary>
    /// Gets the types defined by this plugin.
    /// </summary>
    public IReadOnlyList<IPluginTypeInfo> PluginTypes => _pluginTypes;

    /// <summary>
    /// Gets whether this plugin is implemented locally in the <see cref="StackRepository"/> (not a packaged one).
    /// </summary>
    [MemberNotNullWhen( false, nameof( InformationalVersion ) )]
    public bool IsSourcePlugin => _originalVersion == null;

    /// <summary>
    /// Gets the <see cref="InformationalVersion"/>. Null for a source plugin.
    /// </summary>
    public InformationalVersion? InformationalVersion => _informationalVersion ??= _originalVersion != null
                                                                                    ? new InformationalVersion( _originalVersion )
                                                                                    : null;

    /// <summary>
    /// Returns the <see cref="FullPluginName"/>.
    /// </summary>
    /// <returns>The full plugin name.</returns>
    public override string ToString() => FullPluginName;

}


