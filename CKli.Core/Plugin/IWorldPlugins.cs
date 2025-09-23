using System;
using System.Collections.Generic;
using System.Runtime.Loader;

namespace CKli.Core;

/// <summary>
/// Factory for initialized world's plugins.
/// </summary>
public interface IWorldPlugins : IDisposable
{
    /// <summary>
    /// Gets the plugin basic informations.
    /// </summary>
    IReadOnlyCollection<IWorldPluginInfo> Plugins { get; }

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

