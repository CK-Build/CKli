using CK.Core;
using System;
using System.Runtime.Loader;

namespace CKli.Core;

/// <summary>
/// Factory for <see cref="IPluginCollection"/>.
/// </summary>
public interface IPluginFactory : IDisposable
{
    /// <summary>
    /// Initializes a configured set of plugins for a World. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The world.</param>
    /// <returns>
    /// A disposable that must be called to release the <see cref="AssemblyLoadContext"/> and dispose
    /// plugins that are IDisposable.
    /// </returns>
    IPluginCollection Create( IActivityMonitor monitor, World world );

    /// <summary>
    /// Gets this factory compilation mode.
    /// </summary>
    PluginCompilationMode CompilationMode { get; }

    /// <summary>
    /// Generates the CompiledPlugins code.
    /// Must be called only when <see cref="CompilationMode"/> is <see cref="PluginCompilationMode.None"/>.
    /// </summary>
    /// <returns>The static class CompiledPlugins source code.</returns>
    string GenerateCode();
}

