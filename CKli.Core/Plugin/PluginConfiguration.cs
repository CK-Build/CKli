using System;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Simple wrapper around a <see cref="XElement"/> that enables extension methods
/// to be defined to help reading Plugin configuration.
/// </summary>
public readonly struct PluginConfiguration
{
    readonly XElement _e;

    internal PluginConfiguration( XElement e ) => _e = e;

    /// <summary>
    /// Gets the configuration element.
    /// <para>
    /// A <see cref="InvalidOperationException"/> is thrown by any modification to this element:
    /// the <see cref="PluginConfigurationEditor"/> must be used.
    /// </para>
    /// </summary>
    public XElement XElement => _e;

    /// <summary>
    /// Gets whether this configuration is empty (no child element nor text nodes or comments).
    /// <see cref="XElement.HasAttributes"/> may be true.
    /// </summary>
    public bool IsEmpty => _e.IsEmpty;
}
