using CK.Core;
using System;
using System.Collections.Generic;
using System.Runtime.Loader;

namespace CKli.Core;

/// <summary>
/// Factory for initialized world's plugins.
/// </summary>
public interface IPluginCollection : IDisposable
{
    /// <summary>
    /// Gets the plugin informations.
    /// </summary>
    IReadOnlyCollection<PluginInfo> Plugins { get; }

    /// <summary>
    /// Initializes a configured set of plugins for a World. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The world.</param>
    /// <returns>
    /// A disposable that must be called to release the <see cref="AssemblyLoadContext"/> and dispose
    /// plugins that are IDisposable.
    /// </returns>
    IDisposable Create( IActivityMonitor monitor, World world );

    /// <summary>
    /// Gets whether this collection is already compiled.
    /// </summary>
    bool IsCompiledPlugins { get; }

    /// <summary>
    /// Generates the CompiledPlugins code.
    /// Must be called only when <see cref="IsCompiledPlugins"/> is false.
    /// </summary>
    /// <returns>The static class CompiledPlugins source code.</returns>
    string GenerateCode();
}

