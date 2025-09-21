using CK.Core;
using CSemVer;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace CKli.Core;

sealed class PluginMachinery
{
    readonly string _name;
    readonly NormalizedPath _root;
    readonly NormalizedPath _runFolder;
    readonly NormalizedPath _dllPath;

    NormalizedPath _slnxPath;
    NormalizedPath _ckliPluginsFolder;
    NormalizedPath _directoryBuildProps;
    NormalizedPath _directoryPackageProps;
    NormalizedPath _nugetConfigFile;
    NormalizedPath _ckliPluginsCSProj;
    NormalizedPath _ckliPluginsFile;

    // Result, on success, is set by Create (on error, the failing Machinery is not exposed).
    [AllowNull]IWorldPlugins _worlPlugins;

    public string Name => _name;

    public NormalizedPath Root => _root;

    public NormalizedPath RunFolder => _runFolder;

    public NormalizedPath DllPath => _dllPath;

    public NormalizedPath SlnxPath => _slnxPath.IsEmptyPath ? (_slnxPath = Root.AppendPart( $"{Name}.slnx" )) : _slnxPath;

    public NormalizedPath DirectoryBuildProps => _directoryBuildProps.IsEmptyPath ? (_directoryBuildProps = Root.AppendPart( "Directory.Build.props" )) : _directoryBuildProps;

    public NormalizedPath NuGetConfigFile => _nugetConfigFile.IsEmptyPath ? (_nugetConfigFile = Root.AppendPart( "nuget.config" )) : _nugetConfigFile;

    public NormalizedPath DirectoryPackageProps => _directoryPackageProps.IsEmptyPath ? (_directoryPackageProps = Root.AppendPart( "Directory.Packages.props" )) : _directoryPackageProps;

    public NormalizedPath CKliPluginsFolder => _ckliPluginsFolder.IsEmptyPath ? (_ckliPluginsFolder = Root.AppendPart( "CKli.Plugins" )) : _ckliPluginsFolder;

    public NormalizedPath CKliPluginsCSProj => _ckliPluginsCSProj.IsEmptyPath ? (_ckliPluginsCSProj = CKliPluginsFolder.AppendPart( "CKli.Plugins.csproj" )) : _ckliPluginsCSProj;

    public NormalizedPath CKliPluginsFile => _ckliPluginsFile.IsEmptyPath ? (_ckliPluginsFile = CKliPluginsFolder.AppendPart( "CKli.Plugins.cs" )) : _ckliPluginsFile;

    public IWorldPlugins WorlPlugins => _worlPlugins;

    PluginMachinery( LocalWorldName worldName )
    {
        _name = $"{worldName.StackName}-Plugins{worldName.LTSName}";
        _root = worldName.Stack.StackWorkingFolder.AppendPart( Name );
        _runFolder = worldName.Stack.StackWorkingFolder.Combine( $"$Local/{Name}/bin/CKli.Plugins/run" );
        _dllPath = _runFolder.AppendPart( "CKli.Plugins.dll" );
    }

    internal static PluginMachinery? Create( IActivityMonitor monitor, LocalWorldName worldName, WorldDefinitionFile definitionFile )
    {
        Throw.DebugAssert( !definitionFile.IsPluginsDisabled );
        var machinery = new PluginMachinery( worldName );
        if( !Directory.Exists( machinery.Root ) )
        {
            machinery.CreateSolution( monitor );
            // A newly created solution must compile.
            if( !machinery.EnsureCompiledPlugins( monitor, definitionFile ) )
            {
                return null;
            }
        }
        if( !machinery.CheckCompiledPlugin( monitor ) )
        {
            return null;
        }
        var pluginsConfiguration = definitionFile.ReadPluginsConfiguration( monitor );
        if( pluginsConfiguration == null )
        {
            return null;
        }
        var worldPlugins = PluginLoadContext.Load( monitor, machinery.RunFolder, machinery.DllPath, new PluginCollectorContext( worldName, definitionFile, pluginsConfiguration ) );
        if( worldPlugins == null )
        {
            return null;
        }
        machinery._worlPlugins = worldPlugins;
        return machinery;
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
        File.WriteAllText( NuGetConfigFile, $"""
                <configuration>

                  <packageSources>
                    <clear />

                    <add key="Signature-OpenSource" value="https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json" />
                    <add key="NuGet" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>

                  <packageSourceMapping>
                    <packageSource key="NuGet">
                      <package pattern="*" />
                    </packageSource>
                    <packageSource key="Signature-OpenSource">
                      <package pattern="*" />
                    </packageSource>
                  </packageSourceMapping>

                </configuration>                
                """ );
        File.WriteAllText( DirectoryPackageProps, $"""
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageVersion Include="CKli.Plugins.Core" Version="{World.SafeCKliVersion}" />
                  </ItemGroup>
                </Project>
                
                """ );
        File.WriteAllText( CKliPluginsCSProj, $"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="CKli.Plugins.Core" />
                  </ItemGroup>

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

