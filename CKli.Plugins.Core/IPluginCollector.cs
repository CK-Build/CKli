using System;

namespace CKli.Core;

/// <summary>
/// Supports plugin registration.
/// </summary>
public interface IPluginCollector
{
    /// <summary>
    /// Registers plugins assemblies via their default primary plugins.
    /// </summary>
    /// <returns>The plugin factory.</returns>
    IPluginFactory BuildPluginFactory( ReadOnlySpan<Type> defaultPrimaryPlugins );
}
