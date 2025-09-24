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
    /// Gets the plugin basic informations. Only the default primary plugins appear in this collection.
    /// </summary>
    IReadOnlyCollection<IPluginInfo> Plugins { get; }

    /// <summary>
    /// Initializes a configured set of plugins for a World. 
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>
    /// A disposable that must be called to release the <see cref="AssemblyLoadContext"/> and dispose
    /// plugins that are IDisposable.
    /// </returns>
    IDisposable Create( World world );
}

