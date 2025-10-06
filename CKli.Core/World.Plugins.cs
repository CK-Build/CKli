using CK.Core;
using CSemVer;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

public sealed partial class World
{

    internal CommandNamespace Commands
    {
        get
        {
            Throw.DebugAssert( _plugins != null );
            return _plugins.Commands;
        }
    }

    internal bool SetPluginCompilationMode( IActivityMonitor monitor, PluginCompilationMode mode )
    {
        Throw.DebugAssert( mode != DefinitionFile.CompilationMode );
        using( DefinitionFile.StartEdit() )
        {
            DefinitionFile.CompilationMode = mode;
        }
        return DefinitionFile.SaveFile( monitor );
    }

    internal bool AddOrSetPluginPackage( IActivityMonitor monitor, string packageId, SVersion version )
    {
        if( !CheckAndPrepareForNewPlugin( monitor, packageId, out var shortName, out var fullName ) )
        {
            return false;
        }
        if( !_pluginMachinery.AddOrSetPluginPackage( monitor, shortName, fullName, version, out bool added, out bool versionChanged ) )
        {
            _stackRepository.ResetHard( monitor );
            return false;
        }
        if( !added && !versionChanged )
        {
            return true;
        }
        return _stackRepository.Commit( monitor, added
                                                    ? $"After adding plugin '{fullName}' in version '{version}'."
                                                    : $"After updating plugin '{fullName}' to version '{version}'." );
    }

    internal bool CreatePlugin( IActivityMonitor monitor, string pluginName )
    {
        if( !CheckAndPrepareForNewPlugin( monitor, pluginName, out var shortName, out var fullName ) )
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
        return _stackRepository.Commit( monitor, $"After creating source plugin '{fullName}'." );
    }

    [MemberNotNullWhen(true, nameof(_pluginMachinery))]
    bool CheckAndPrepareForNewPlugin( IActivityMonitor monitor,
                                      string pluginName,
                                      [NotNullWhen( true )] out string? shortName,
                                      [NotNullWhen( true )] out string? fullName )
    {
        Throw.CheckState( !DefinitionFile.IsPluginsDisabled );
        Throw.DebugAssert( _pluginMachinery != null );
        if( !PluginMachinery.EnsureFullPluginName( monitor, pluginName, out shortName, out fullName ) )
        {
            return false;
        }
        var localShortName = shortName;
        var config = DefinitionFile.Plugins.Elements().FirstOrDefault( e => e.Name.LocalName.Equals( localShortName, StringComparison.OrdinalIgnoreCase ) );
        if( config != null )
        {
            monitor.Error( $"A Plugin '{config.Name.LocalName}' already exists in <Plugins /> of '{Name.XmlDescriptionFilePath}'." );
            return false;
        }
        // Commit Stack before operations.
        if( !_stackRepository.Commit( monitor, $"Before new plugin '{fullName}'." ) )
        {
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
        return _stackRepository.Commit( monitor, $"After removing plugin '{fullName}'." );
    }
}
