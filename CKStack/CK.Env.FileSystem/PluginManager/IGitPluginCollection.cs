using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Plugin collection of <see cref="IGitPlugin"/> or <see cref="IGitBranchPlugin"/>.
    /// </summary>
    /// <typeparam name="T">Actual plugin type.</typeparam>
    public interface IGitPluginCollection<T> : IReadOnlyCollection<T>
        where T : class
    {
        /// <summary>
        /// Gets the branch name. This is null for root plugins.
        /// </summary>
        string BranchName { get; }

        /// <summary>
        /// Gets the service container for this <see cref="BranchName"/>
        /// or the <see cref="GitPluginManager.ServiceContainer"/> when <see cref="BranchName"/> is null.
        /// </summary>
        SimpleServiceContainer ServiceContainer { get; }

        /// <summary>
        /// Gets a plugin.
        /// </summary>
        /// <param name="t">Type of the plugin.</param>
        /// <returns>The plugin instance or null.</returns>
        T GetPlugin( Type t );

        /// <summary>
        /// Typed version of <see cref="GetPlugin(Type)"/>.
        /// </summary>
        /// <typeparam name="P">Type of the plugin.</typeparam>
        /// <returns>The plugin instance or null.</returns>
        P GetPlugin<P>() where P : T;
    }
}
