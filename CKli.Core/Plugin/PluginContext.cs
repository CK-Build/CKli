using CK.Core;
using CSemVer;
using System.IO;

namespace CKli.Core;


sealed class PluginContext
{
    readonly string _name;
    readonly NormalizedPath _root;
    readonly NormalizedPath _dllPath;

    NormalizedPath _slnxPath;
    NormalizedPath _ckliPluginsFolder;
    NormalizedPath _directoryBuildProps;
    NormalizedPath _directoryPackageProps;
    NormalizedPath _ckliPluginsCSProj;
    NormalizedPath _ckliPluginsFile;

    public string Name => _name;

    public NormalizedPath Root => _root;

    public NormalizedPath DllPath => _dllPath;

    public NormalizedPath SlnxPath => _slnxPath.IsEmptyPath ? (_slnxPath = Root.AppendPart( $"{Name}.slnx" )) : _slnxPath;

    public NormalizedPath DirectoryBuildProps => _directoryBuildProps.IsEmptyPath ? (_directoryBuildProps = Root.AppendPart( "Directory.Build.props" )) : _directoryBuildProps;

    public NormalizedPath DirectoryPackageProps => _directoryPackageProps.IsEmptyPath ? (_directoryPackageProps = Root.AppendPart( "Directory.Package.props" )) : _directoryPackageProps;

    public NormalizedPath CKliPluginsFolder => _ckliPluginsFolder.IsEmptyPath ? (_ckliPluginsFolder = Root.AppendPart( "CKli.Plugins" )) : _ckliPluginsFolder;

    public NormalizedPath CKliPluginsCSProj => _ckliPluginsCSProj.IsEmptyPath ? (_ckliPluginsCSProj = CKliPluginsFolder.AppendPart( "CKli.Plugins.csproj" )) : _ckliPluginsCSProj;

    public NormalizedPath CKliPluginsFile => _ckliPluginsFile.IsEmptyPath ? (_ckliPluginsFile = CKliPluginsFolder.AppendPart( "CKli.Plugins.cs" )) : _ckliPluginsFile;

    PluginContext( LocalWorldName worldName )
    {
        _name = "Plugins" + worldName.LTSName;
        _root = worldName.Stack.StackWorkingFolder.AppendPart( Name );
        _dllPath = worldName.Stack.StackWorkingFolder.Combine( $"$Local/{Name}/bin/CKli.Plugins/run/CKli.Plugins.dll" );
    }

    internal static IWorldPlugins? Create( IActivityMonitor monitor, LocalWorldName worldName, WorldDefinitionFile definitionFile )
    {
        Throw.DebugAssert( !definitionFile.IsPluginsDisabled );
        var manager = new PluginContext( worldName );
        if( !Directory.Exists( manager.Root ) )
        {
            manager.CreateSolution( monitor );
            // A newly created solution must compile.
            if( !manager.EnsureCompiledPlugins( monitor, definitionFile ) )
            {
                return null;
            }
        }
        if( !manager.CheckCompiledPlugin( monitor ) )
        {
            return null;
        }
        var pluginsConfiguration = definitionFile.ReadPluginsConfiguration( monitor );
        if( pluginsConfiguration == null )
        {
            return null;
        }
        return PluginLoadContext.Load( monitor, manager.DllPath, new PluginCollectorContext( worldName, definitionFile, pluginsConfiguration ) );
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
        File.WriteAllText( DirectoryPackageProps, $"""
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageVersion Include="CKli.Plugins.Core" Version="{World.CKliVersion.Version}" />
                  </ItemGroup>
                </Project>
                
                """ );
        File.WriteAllText( CKliPluginsCSProj, $"""
                <Project Sdk="Microsoft.NET.Sdk">

                </Project>
                
                """ );
        File.WriteAllText( CKliPluginsFile, """
                using CKli.Core;

                namespace CKli.Plugins;

                public static class Plugins
                {
                    public static IWorldPlugins Register( PluginCollectorContext ctx )
                    {
                        var collector = PluginCollector.Create( ctx );
                        return collector.Build();
                    }
                }
                                
                """ );
    }

    bool CheckCompiledPlugin( IActivityMonitor monitor )
    {
        if( !File.Exists( DllPath ) )
        {
            monitor.Error( $"Expected compliled plugin file not found: {DllPath}" );
            return false;
        }
        return true;
    }

    bool EnsureCompiledPlugins( IActivityMonitor monitor, WorldDefinitionFile definitionFile )
    {
        int exitCode = ProcessRunner.RunProcess( monitor.ParallelLogger,
                                                 "dotnet",
                                                 "build",
                                                 Root,
                                                 environmentVariables: null );
        if( exitCode != 0 )
        {
            monitor.Error( $"Failed to build '{Name}' solution. Exit code = '{exitCode}'." );
            return false;
        }
        return CheckCompiledPlugin( monitor );
    }

}

