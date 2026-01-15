using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CKli.Core;

public sealed partial class PluginMachinery
{
    /// <summary>
    /// Gets or sets an optional transformer of the "nuget.config" file.
    /// <para>
    /// This is mainly for tests.
    /// </para>
    /// </summary>
    public static Action<IActivityMonitor, XDocument>? NuGetConfigFileHook
    {
        get => _nuGetConfigFileHook;
        set => _nuGetConfigFileHook = value;
    }

    /// <summary>
    /// Returns the "<see cref="WorldName.StackName"/>-Plugins<see cref="WorldName.LTSName"/>" string.
    /// </summary>
    /// <param name="name">The world name.</param>
    /// <returns>The plugins solution name.</returns>
    public static string GetPluginSolutionName( WorldName name ) => $"{name.StackName}-Plugins{name.LTSName}";

    /// <summary>
    /// Returns the string "$Local/<see cref="GetPluginSolutionName(WorldName)"/>/bin/CKli.Plugins/run".
    /// </summary>
    /// <param name="pluginSolutionName">The plugins solution name.</param>
    /// <returns>The sub folder that contains the compiled plugins.</returns>
    public static string GetLocalRunFolder( string pluginSolutionName ) => $"$Local/{pluginSolutionName}/bin/CKli.Plugins/run";

    /// <summary>
    /// Helper that checks and normalizes a plugin name.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="pluginName">The candidate name.</param>
    /// <param name="shortPluginName">The short plugin name on success.</param>
    /// <param name="fullPluginName">The full plugin name on success.</param>
    /// <returns>True on success, false if the plugin name is invalid.</returns>
    public static bool EnsureFullPluginName( IActivityMonitor monitor,
                                             string? pluginName,
                                             [NotNullWhen( true )] out string? shortPluginName,
                                             [NotNullWhen( true )] out string? fullPluginName )
    {
        if( !EnsureFullPluginName( pluginName, out shortPluginName, out fullPluginName ) )
        {
            monitor.Error( $"Invalid plugin name '{pluginName}'. Must be '[A-Za-z][A-Za-z0-9]' or 'Ckli.[A-Za-z][A-Za-z0-9].Plugin'." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Helper that checks and normalizes a plugin name.
    /// </summary>
    /// <param name="pluginName">The candidate name.</param>
    /// <param name="shortPluginName">The short plugin name on success.</param>
    /// <param name="fullPluginName">The full plugin name on success.</param>
    /// <returns>True on success, false if the plugin name is invalid.</returns>
    public static bool EnsureFullPluginName( string? pluginName,
                                             [NotNullWhen( true )] out string? shortPluginName,
                                             [NotNullWhen( true )] out string? fullPluginName )
    {
        if( IsValidFullPluginName( pluginName ) )
        {
            fullPluginName = pluginName;
            shortPluginName = fullPluginName[5..^7];
            return true;
        }
        if( IsValidShortPluginName( pluginName ) )
        {
            fullPluginName = $"CKli.{pluginName}.Plugin";
            Throw.DebugAssert( IsValidFullPluginName( fullPluginName ) );
            shortPluginName = pluginName;
            return true;
        }
        if( pluginName != null )
        {
            if( pluginName.StartsWith( "CKli.", StringComparison.OrdinalIgnoreCase ) )
            {
                pluginName = pluginName.Substring( 5 );
            }
            if( pluginName.EndsWith( ".Plugin", StringComparison.OrdinalIgnoreCase ) )
            {
                pluginName = pluginName[..^7];
            }
            if( IsValidShortPluginName( pluginName ) )
            {
                fullPluginName = $"CKli.{pluginName}.Plugin";
                Throw.DebugAssert( IsValidFullPluginName( fullPluginName ) );
                shortPluginName = pluginName;
                return true;
            }
        }
        shortPluginName = null;
        fullPluginName = null;
        return false;
    }

    /// <summary>
    /// Gets whether this name is a valid "CKli.XXX.Plugin" name.
    /// </summary>
    /// <param name="pluginName">The name to test.</param>
    /// <returns>True if this is a valid full plugin name.</returns>
    public static bool IsValidFullPluginName( [NotNullWhen( true )] string? pluginName )
    {
        return pluginName != null && ValidFullPluginName().Match( pluginName ).Success;
    }

    /// <summary>
    /// Gets whether this name is a valid short "XXX" plugin name: starts
    /// with [A-Za-z] followed by at least 2 [A-Za-z0-9].
    /// </summary>
    /// <param name="pluginName">The name to test.</param>
    /// <returns>True if this is a valid short plugin name.</returns>
    public static bool IsValidShortPluginName( [NotNullWhen( true )] string? pluginName )
    {
        return pluginName != null && ValidShortPluginName().Match( pluginName ).Success;
    }

    [GeneratedRegex( @"^CKli\.[A-Za-z][A-Za-z0-9]{2,}\.Plugin$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex ValidFullPluginName();

    [GeneratedRegex( @"^[A-Za-z][A-Za-z0-9]{2,}$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex ValidShortPluginName();

}

