using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// Describes <see cref="IPluginTypeInfo.Status"/>.
/// </summary>
public enum PluginStatus
{
    /// <summary>
    /// The plugin is available.
    /// </summary>
    Available,

    /// <summary>
    /// The plugin is disabled in the &lt;Plugins&gt; configuration.
    /// </summary>
    DisabledByConfiguration = 1,

    /// <summary>
    /// The plugin is disabled because at least one of its required dependency
    /// is disabled.
    /// </summary>
    DisabledByDependency = 2,

    /// <summary>
    /// An implementation exists but its name doesn't appear in the &lt;Plugins&gt; configuration.
    /// </summary>
    DisabledByMissingConfiguration = 4,

    /// <summary>
    /// The plugin name appears in the &lt;Plugins&gt; configuration but is not
    /// available.
    /// </summary>
    MissingImplementation = 8,

}

/// <summary>
/// Extends <see cref="PluginStatus"/>
/// </summary>
public static class PluginStatusExtensions
{
    /// <summary>
    /// Gets whether this status is a disabled one.
    /// </summary>
    /// <param name="status">This status.</param>
    /// <returns>True if this status is disabled.</returns>
    public static bool IsDisabled( this PluginStatus status )
    {
        return (status & (PluginStatus.DisabledByConfiguration
                         | PluginStatus.DisabledByMissingConfiguration
                         | PluginStatus.DisabledByDependency)) != 0;
    }

    /// <summary>
    /// Gets a readable text for this status.
    /// </summary>
    /// <param name="status">This status.</param>
    /// <returns>A readable string.</returns>
    public static string GetTextStatus( this PluginStatus status )
    {
        if( status == PluginStatus.MissingImplementation )
            return "⚠ Configured but no plugin installed.";
        if( status == PluginStatus.Available )
            return "Available";
        if( (status & PluginStatus.DisabledByMissingConfiguration) != 0 )
            return "⚠ Disabled, missing configuration.";
        if( (status & PluginStatus.DisabledByConfiguration) != 0 )
            return "Disabled by configuration.";
        if( (status & PluginStatus.DisabledByDependency) != 0 )
            return "Disabled by disabled dependencies.";
        return Throw.CKException<string>( "Invalid status." );
    }
    public static Color GetStatusColor( this PluginStatus status )
    {
        if( status == PluginStatus.MissingImplementation )
            return new Color( ConsoleColor.Black, ConsoleColor.Red );
        if( status == PluginStatus.Available )
            return new Color( ConsoleColor.Green, ConsoleColor.Black );
        if( (status & PluginStatus.DisabledByMissingConfiguration) != 0 )
            return new Color( ConsoleColor.Black, ConsoleColor.Red );
        if( (status & PluginStatus.DisabledByConfiguration) != 0 )
            return new Color( ConsoleColor.Yellow, ConsoleColor.Black );
        if( (status & PluginStatus.DisabledByDependency) != 0 )
            return new Color( ConsoleColor.Yellow, ConsoleColor.Black );
        return Throw.CKException<Color>( "Invalid status." );
    }
}

