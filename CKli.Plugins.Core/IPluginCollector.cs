using System;
using System.Reflection;

namespace CKli.Core;

/// <summary>
/// Supports plugin registration.
/// </summary>
public interface IPluginCollector
{
    /// <summary>
    /// Registers plugins assemblies via their default primary plugins.
    /// </summary>
    /// <returns>The plugin collection.</returns>
    IPluginCollection BuildPluginCollection( ReadOnlySpan<Type> defaultPrimaryPlugins );
}
