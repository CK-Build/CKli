using CK.Core;
using System;
using System.IO;
using System.Reflection;

namespace CKli.Core;

sealed class PluginManager
{
    readonly LocalWorldName _worldName;
    PluginCollector? _plugins;

    readonly string _name;
    readonly NormalizedPath _root;
    readonly NormalizedPath _dllPath;

    NormalizedPath _slnxPath;
    NormalizedPath _ckliPluginsFolder;
    NormalizedPath _directoryBuildProps;
    NormalizedPath _ckliPluginsCSProj;
    NormalizedPath _ckliPluginsFile;

    public string Name => _name;

    public NormalizedPath Root => _root;

    public NormalizedPath DllPath => _dllPath;

    public NormalizedPath SlnxPath => _slnxPath.IsEmptyPath ? (_slnxPath = Root.AppendPart( $"{Name}.slnx" )) : _slnxPath;

    public NormalizedPath DirectoryBuildProps => _directoryBuildProps.IsEmptyPath ? (_directoryBuildProps = Root.AppendPart( "Directory.Build.props" )) : _directoryBuildProps;

    public NormalizedPath CKliPluginsFolder => _ckliPluginsFolder.IsEmptyPath ? (_ckliPluginsFolder = Root.AppendPart( "CKli.Plugins" )) : _ckliPluginsFolder;

    public NormalizedPath CKliPluginsCSProj => _ckliPluginsCSProj.IsEmptyPath ? (_ckliPluginsCSProj = CKliPluginsFolder.AppendPart( "CKli.Plugins.csproj" )) : _ckliPluginsCSProj;

    public NormalizedPath CKliPluginsFile => _ckliPluginsFile.IsEmptyPath ? (_ckliPluginsFile = CKliPluginsFolder.AppendPart( "CKli.Plugins.cs" )) : _ckliPluginsFile;

    PluginManager( LocalWorldName worldName )
    {
        _worldName = worldName;
        _name = "Plugins" + worldName.LTSName;
        _root = worldName.Stack.StackWorkingFolder.AppendPart( Name );
        _dllPath = worldName.Stack.StackWorkingFolder.Combine( $"$Local/{Name}/bin/CKli.Plugins/run/CKli.Plugins.dll" );
    }

    internal static PluginManager? Create( IActivityMonitor monitor, LocalWorldName worldName, WorldDefinitionFile definitionFile )
    {
        Throw.DebugAssert( !definitionFile.IsPluginsDisabled );
        var manager = new PluginManager( worldName );
        if( !Directory.Exists( manager.Root ) )
        {
            manager.CreateSolution( monitor );
        }
        if( !manager.SetupPlugins( monitor ) )
        {
            return null;
        }
        return manager;
    }

    void CreateSolution( IActivityMonitor monitor )
    {
        monitor.Info( $"Creating '{Name}' solution." );
        Directory.CreateDirectory( Root );
        File.WriteAllText( SlnxPath, """
                <Solution>
                  <Configurations>
                    <Platform Name="Any CPU" />
                  </Configurations>
                  <Project Path="CKli.Plugins/CKli.Plugins.csproj" />
                </Solution>
                """ );
        Directory.CreateDirectory( CKliPluginsFolder );
        File.WriteAllText( DirectoryBuildProps, $"""
                <Project>
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ArtifactsPath>$(MSBuildThisFileDirectory)../$Local/{Name}</ArtifactsPath>
                    <ArtifactsPivots>run</ArtifactsPivots>
                  </PropertyGroup>
                </Project>
                
                """ );
        File.WriteAllText( CKliPluginsCSProj, $"""
                <Project Sdk="Microsoft.NET.Sdk">

                </Project>
                
                """ );
        File.WriteAllText( CKliPluginsFile, """
                using CKli.Core;

                namespace CKli.Plugin;

                public static class Plugin
                {
                    public static void Register( WorldServiceCollection services )
                    {
                    }
                }
                
                """ );
    }

    bool SetupPlugins( IActivityMonitor monitor )
    {
        Throw.DebugAssert( _plugins == null );
        try
        {
            var a = Assembly.LoadFrom( DllPath );
            var services = new PluginCollector();
            a.GetType( "CKli.Plugin.Plugin" )!.GetMethod( "Register" )!.Invoke( null, [services] );
            _plugins = services;
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "While initializing plugins.", ex );
            return false;
        }
    }
}

