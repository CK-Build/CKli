using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Provides plugin type level informations.
/// </summary>
public interface IPluginTypeInfo
{
    /// <summary>
    /// Gets the plugin that defines this type.
    /// </summary>
    IPluginInfo Plugin { get; }

    /// <summary>
    /// Gets the csharp type name.
    /// </summary>
    string TypeName { get; }

    /// <summary>
    /// Gets whether this is a primary or a support plugin type.
    /// </summary>
    bool IsPrimary { get; }

    /// <summary>
    /// Gets the status flags.
    /// </summary>
    PluginStatus Status { get; }
}

