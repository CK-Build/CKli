using ConsoleAppFramework;
using System;

namespace CKli;

[RegisterCommands( "plugin" )]
public sealed class PluginCommand
{
    /// <summary>
    /// Creates a new source based plugin project for the current World.
    /// </summary>
    /// <param name="pluginName">The plugin name "MyPlugin" (or "CKli.MyPlugin.Plugin") to create.</param>
    /// <param name="allowLts">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Create( [Argument] string pluginName, bool allowLts = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.PluginCreate( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, pluginName, allowLts );
        } );
    }

    /// <summary>
    /// Dumps information about installed plugins.
    /// </summary>
    /// <returns>0 on success, negative on error.</returns>
    public int Info()
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.PluginInfo( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory );
        } );
    }

    /// <summary>
    /// Adds a new plugin (or upgrade an existing one) to the current world's plugins. 
    /// </summary>
    /// <param name="package">Package "Name@Version" or ""CKli.Name.Plugin@Version" to add. Actual CKli plugin packages are "CKli.XXX.Plugin".</param>
    /// <param name="allowLts">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Add( [Argument, PackageInstanceParser] PackageInstance package, bool allowLts = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.PluginAdd( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, package.PackageId, package.Version, allowLts );
        } );
    }

}
