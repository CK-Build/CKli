using CK.Core;
using CSemVer;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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
        if( _pluginMachinery != null )
        {
            return _pluginMachinery.SetPluginCompilationMode( monitor, this, mode );
        }
        _definitionFile.SetPluginCompilationMode( monitor, mode );
        return true;
    }

    internal bool AddOrSetPluginPackage( IActivityMonitor monitor, string packageId, SVersion version )
    {
        // This creates a Stack Commit if the Stack's working folder is dirty.
        if( !CheckAndPrepareForNewPlugin( monitor, packageId, createMode: false, out var shortName, out var fullName ) )
        {
            return false;
        }
        if( !_pluginMachinery.AddOrSetPluginPackage( monitor, this, shortName, fullName, version, out bool added, out bool versionChanged )
            || !_definitionFile.SaveFile( monitor ) )
        {
            _stackRepository.ResetHard( monitor );
            return false;
        }
        return !added && !versionChanged
                || _stackRepository.Commit( monitor, added
                                                      ? $"Added plugin '{fullName}' in version '{version}'."
                                                      : $"Updated plugin '{fullName}' to version '{version}'." );
    }

    internal bool CreatePlugin( IActivityMonitor monitor, string pluginName )
    {
        if( !CheckAndPrepareForNewPlugin( monitor, pluginName, createMode: true, out var shortName, out var fullName ) )
        {
            return false;
        }
        if( !_pluginMachinery.CreatePlugin( monitor, this, shortName, fullName )
            || !_definitionFile.SaveFile( monitor ) )
        {
            _stackRepository.ResetHard( monitor );
            return false;
        }
        return _stackRepository.Commit( monitor, $"Created source plugin '{fullName}'." );
    }

    [MemberNotNullWhen(true, nameof(_pluginMachinery))]
    bool CheckAndPrepareForNewPlugin( IActivityMonitor monitor,
                                      string pluginName,
                                      bool createMode,
                                      [NotNullWhen( true )] out string? shortName,
                                      [NotNullWhen( true )] out string? fullName )
    {
        Throw.DebugAssert( _pluginMachinery != null );
        if( !PluginMachinery.EnsureFullPluginName( monitor, pluginName, out shortName, out fullName ) )
        {
            return false;
        }
        var localShortName = shortName;
        if( createMode )
        {
            var config = DefinitionFile.Plugins.Elements().FirstOrDefault( e => e.Name.LocalName.Equals( localShortName, StringComparison.OrdinalIgnoreCase ) );
            if( config != null )
            {
                monitor.Error( $"A Plugin '{config.Name.LocalName}' already exists in <Plugins /> of '{Name.XmlDescriptionFilePath}'." );
                return false;
            }
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
        Throw.DebugAssert( _pluginMachinery != null );
        if( !PluginMachinery.EnsureFullPluginName( monitor, pluginName, out var shortName, out var fullName ) )
        {
            return false;
        }
        // Commit Stack before operations.
        if( !_stackRepository.Commit( monitor, $"Before removing plugin '{fullName}'." ) )
        {
            return false;
        }
        if( !_pluginMachinery.RemovePlugin( monitor, this, shortName, fullName )
            || !_definitionFile.SaveFile( monitor ) )
        {
            _stackRepository.ResetHard( monitor );
            return false;
        }
        return _stackRepository.Commit( monitor, $"Removed plugin '{fullName}'." );
    }
}
