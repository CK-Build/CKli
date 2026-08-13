using CK.Core;
using System;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Required base class for plugins.
/// <para>
/// Plugins can implement <see cref="IDisposable"/> if needed.
/// Dispose will be called before unloading it.
/// </para>
/// <para>
/// A plugin that specializes this class is "optional": it will be instantiated only if required by
/// another plugin constructor. Use the <see cref="PrimaryPluginBase"/> as the base class for a
/// primary plugin that will be instantiated by default.
/// </para>
/// </summary>
public abstract class PluginBase
{
    /// <summary>
    /// A list of standard plugin names (co-released with CKli).
    /// Their versions are automatically updated when CKli is updated: the <see cref="PluginMachinery"/> checks
    /// that the CKli.Plugins.Core referenced by the CKli.Plugins project has the same version as CKli.Core itself
    /// that is <see cref="World.CKliVersion"/>.
    /// <para>
    /// When the version differs, the PackageReference of the CKli.Plugins project and all source-based projects are
    /// updated to use the <see cref="World.CKliVersion"/>. The assemblies listed here are managed identically.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> StandardPluginNames =
        [
            "CKli.ArtifactHandler.Plugin",
            "CKli.BranchModel.Plugin",
            "CKli.Build.Plugin",
            "CKli.CommonFiles.Plugin",
            "CKli.HotZone.Plugin",
            "CKli.Migration.Plugin",
            "CKli.Publish.Plugin",
            "CKli.ShallowSolution.Plugin",
            "CKli.VersionTag.Plugin"
        ];

    readonly World _world;

    /// <summary>
    /// Initializes a plugin.
    /// </summary>
    /// <param name="world">The world.</param>
    protected PluginBase( World world )
    {
        _world = world;
    }

    /// <summary>
    /// Gets the world.
    /// </summary>
    protected World World => _world;

    /// <summary>
    /// Optional extension point called once all plugins have been instantiated in the
    /// order of the dependencies. Does nothing by default and returns true. 
    /// <para>
    /// Returning false here is a strong error that prevents the load of the plugins.
    /// </para>
    /// <para>
    /// For <see cref="PrimaryPluginBase"/> (or <see cref="PrimaryRepoPlugin{T}"/>) This can typically be used to
    /// initialize an empty configuration or migrate it.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    internal protected virtual bool Initialize( IActivityMonitor monitor ) => true;

    /// <summary>
    /// Static centralized helper that handles integer parameter parsing.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="optionName">The option name ("--max-dop").</param>
    /// <param name="oValue">The option value.</param>
    /// <param name="result">The resulting value to use.</param>
    /// <param name="defaultValue">The default value (when <paramref name="oValue"/> is null).</param>
    /// <param name="minValue">The minimal allowed value.</param>
    /// <param name="maxValue">The maximal allowed value.</param>
    /// <returns>True on success, false on error.</returns>
    internal protected static bool ParseInteger( IActivityMonitor monitor, string optionName, string? oValue, out int result, int defaultValue, int? minValue = 0, int? maxValue = null )
    {
        Throw.CheckArgument( minValue == null || maxValue == null || minValue <= maxValue ); 
        if( oValue == null ) result = defaultValue;
        else if( !int.TryParse( oValue, out result )
                 || (minValue is not null && result < minValue.Value)
                 || (maxValue is not null && result > maxValue.Value) )
        {
            if( minValue is not null )
            {
                if( maxValue is not null )
                {
                    monitor.Error( $"Invalid {optionName} value. Must be an integer between {minValue} and {maxValue} included." );
                }
                else
                {
                    monitor.Error( $"Invalid {optionName} value. Must be an integer greater or equal to {minValue}." );
                }
            }
            else
            {
                if( maxValue is not null )
                {
                    monitor.Error( $"Invalid {optionName} value. Must be an integer lower or equal to {maxValue}." );
                }
                else
                {
                    monitor.Error( $"Invalid {optionName} value. Must be an integer." );
                }
            }
            return false;
        }
        return true;
    }


}
