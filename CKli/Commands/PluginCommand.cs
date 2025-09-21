using ConsoleAppFramework;
using System;

namespace CKli;

[RegisterCommands( "plugin" )]
public sealed class PluginCommand
{
    /// <summary>
    /// Adds a new plugin (or upgrade an existing one) to the current world's plugins. 
    /// </summary>
    /// <param name="package">Package name@version to add.</param>
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
