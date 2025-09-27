using System.Collections.Immutable;

namespace CKli.Core;

public sealed class PluginInfo : IPluginInfo
{
    readonly string _fullPluginName;
    readonly string _pluginName;
    readonly PluginStatus _status;

    public PluginInfo( string fullPluginName, string pluginName, PluginStatus status )
    {
        _fullPluginName = fullPluginName;
        _pluginName = pluginName;
        _status = status;
    }

    public string FullPluginName => _fullPluginName;

    public string PluginName => _pluginName;

    public PluginStatus Status => _status;
}

