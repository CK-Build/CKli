using System;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Contains configured plugin instances bound to a <see cref="World"/>.
/// <para>
/// This collection doesn't surface publicly, it is encapsulated by the <see cref="World"/>
/// and the <see cref="PluginMachinery"/>.
/// </para>
/// </summary>
public interface IPluginCollection : IDisposable
{
    /// <summary>
    /// Gets the plugin informations.
    /// </summary>
    IReadOnlyCollection<PluginInfo> Plugins { get; }

    /// <summary>
    /// Gets the the commands supported by the plugins in addition to the intrinsic CKli commands.
    /// </summary>
    CommandNamespace Commands { get; }
}

