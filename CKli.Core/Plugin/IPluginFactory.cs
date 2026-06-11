using CK.Core;
using System;
using System.Runtime.Loader;

namespace CKli.Core;

/// <summary>
/// Factory for <see cref="PluginCollection"/>.
/// <para>
/// A reflection based implementation (<see cref="CompileMode"/> is <see cref="PluginCompileMode.None"/>) can
/// create a configured and operational plugin collection that uses reflection to call the plugins
/// or <see cref="GenerateCode()"/> can be used to create the source code of a ready-to-run configured
/// plugins graph.
/// </para>
/// <para>
/// A code generated implementation (<see cref="CompileMode"/> is <see cref="PluginCompileMode.Debug"/> or <see cref="PluginCompileMode.Release"/>)
/// can only <see cref="Create(IActivityMonitor, CKli.Core.World)"/> its ready-to-run configured plugin collection
/// and throws a <see cref="InvalidOperationException"/> if <see cref="GenerateCode"/> is called.
/// </para>
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
    PluginCollection Create( IActivityMonitor monitor, World world );

    /// <summary>
    /// Gets this factory compilation mode.
    /// </summary>
    PluginCompileMode CompileMode { get; }

    /// <summary>
    /// Generates the CompiledPlugins code.
    /// Must be called only when <see cref="CompileMode"/> is <see cref="PluginCompileMode.None"/>.
    /// </summary>
    /// <returns>The static class CompiledPlugins source code.</returns>
    string GenerateCode();
}

