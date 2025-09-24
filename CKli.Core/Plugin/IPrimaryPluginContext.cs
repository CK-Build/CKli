using System;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Constructor parameter of a primary plugin.
/// </summary>
public interface IPrimaryPluginContext
{
    /// <summary>
    /// Gets the plugin info.
    /// </summary>
    IPluginInfo PluginInfo { get; }

    /// <summary>
    /// Gets the world.
    /// </summary>
    World World { get; }

    /// <summary>
    /// Gets the xml configuration element.
    /// This must not be altered otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    XElement XmlConfiguration { get; }
}
