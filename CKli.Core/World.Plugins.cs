using CK.Core;
using CSemVer;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

public sealed partial class World
{
    internal bool AddPlugin( IActivityMonitor monitor, string packageId, SVersion version )
    {
        Throw.CheckState( !DefinitionFile.IsPluginsDisabled );
        Throw.DebugAssert( _pluginMachinery != null );
        if( !PluginMachinery.EnsureFullPluginName( monitor, packageId, out var shortName, out var fullName ) )
        {
            return false;
        }
        monitor.Error( "Not implemented yet." );
        return false;
    }

    internal bool CreatePlugin( IActivityMonitor monitor, string pluginName )
    {
        Throw.CheckState( !DefinitionFile.IsPluginsDisabled );
        Throw.DebugAssert( _pluginMachinery != null );
        if( !PluginMachinery.EnsureFullPluginName( monitor, pluginName, out var shortName, out var fullName ) )
        {
            return false;
        }
        var config = DefinitionFile.Plugins.Elements().FirstOrDefault( e => e.Name.LocalName.Equals( shortName, StringComparison.OrdinalIgnoreCase ) );
        if( config != null )
        {
            monitor.Error( $"A Plugin '{config.Name.LocalName}' already exists in <Plugins /> of '{Name.XmlDescriptionFilePath}'." );
            return false;
        }
        // Commit Stack before operations.
        if( !_stackRepository.Commit( monitor, $"Before creating plugin '{fullName}'." ) )
        {
            return false;
        }
        if( !_pluginMachinery.CreatePlugin( monitor, shortName, fullName ) )
        {
            _stackRepository.ResetHard( monitor );
            return false;
        }
        using( DefinitionFile.StartEdit() )
        {
            DefinitionFile.Plugins.Add( new XElement( shortName ) );
        }
        if( !DefinitionFile.SaveFile( monitor ) )
        {
            _stackRepository.ResetHard( monitor );
            return false;
        }
        return true;
    }

    internal bool RemovePlugin( IActivityMonitor monitor, string pluginName )
    {
        Throw.CheckState( !DefinitionFile.IsPluginsDisabled );
        Throw.DebugAssert( _pluginMachinery != null );
        if( !PluginMachinery.EnsureFullPluginName( monitor, pluginName, out var shortName, out var fullName ) )
        {
            return false;
        }
        var config = DefinitionFile.Plugins.Elements().FirstOrDefault( e => e.Name.LocalName.Equals( shortName, StringComparison.OrdinalIgnoreCase ) );
        if( config == null )
        {
            monitor.Warn( $"""
                Plugin '{shortName}' not found in <Plugins /> of '{Name.XmlDescriptionFilePath}'.
                Removing it from implementations even if it has no configuration.
                """ );
        }
        // Commit Stack before operations.
        if( !_stackRepository.Commit( monitor, $"Before removing plugin '{fullName}'." ) )
        {
            return false;
        }
        if( !_pluginMachinery.RemovePlugin( monitor, shortName, fullName ) )
        {
            _stackRepository.ResetHard( monitor );
            return false;
        }
        if( config != null )
        {
            using( DefinitionFile.StartEdit() )
            {
                config.Remove();
            }
            if( !DefinitionFile.SaveFile( monitor ) )
            {
                _stackRepository.ResetHard( monitor );
                return false;
            }
        }
        return true;
    }
}
