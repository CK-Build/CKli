using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Supports the Plugin infrastructure.
/// This class is public only for tests purposes.
/// <para>
/// This class holds the reference to the initialized <see cref="IPluginCollection"/>.
/// </para>
/// </summary>
public sealed partial class PluginMachinery
{
    readonly string _name;
    readonly NormalizedPath _root;
    readonly NormalizedPath _runFolder;
    readonly NormalizedPath _runFolderVNext;
    readonly NormalizedPath _dllPath;
    readonly NormalizedPath _dllPathVNext;
    readonly WorldDefinitionFile _definitionFile;
    NormalizedPath _slnxPath;
    NormalizedPath _ckliPluginsFolder;
    NormalizedPath _directoryBuildProps;
    NormalizedPath _directoryPackageProps;
    NormalizedPath _nugetConfigFile;
    NormalizedPath _ckliPluginsCSProj;
    NormalizedPath _ckliPluginsFile;
    NormalizedPath _ckliCompiledPluginsFile;

    // Fundamental singleton!
    // This MAY be transformed in a dictionary per World (key would be the RunFolder) to allow more than a
    // World to exist at the same time in the process...
    // But this would bring a lot of complexities for no real benefits: CKli is a World tool
    // not a server for multiple Worlds.
    static WeakReference? _singleRunning;

    // Caches whether "Directory.Packages.props" contains the <PackageVersion Include="CKli.Plugins.Core" Version="..." />
    // that is World.CKliVersion.Version. This check is done only once.
    static HashSet<string>? _versionChecked;

    static Action<IActivityMonitor, XDocument>? _nuGetConfigFileHook;

    // Result, on success, is set by Create (on error, the failing Machinery is not exposed).
    [AllowNull] IPluginCollection _worlPlugins;

    /// <summary>
    /// Gets an identifier for the "<see cref="WorldName"/> Plugins" environment.
    /// </summary>
    public string Name => _name;

    internal NormalizedPath Root => _root;

    internal NormalizedPath RunFolder => _runFolder;

    internal NormalizedPath DllPath => _dllPath;

    internal NormalizedPath SlnxPath => _slnxPath.IsEmptyPath ? (_slnxPath = Root.AppendPart( $"{Name}.slnx" )) : _slnxPath;

    internal NormalizedPath DirectoryBuildProps => _directoryBuildProps.IsEmptyPath ? (_directoryBuildProps = Root.AppendPart( "Directory.Build.props" )) : _directoryBuildProps;

    internal NormalizedPath NuGetConfigFile => _nugetConfigFile.IsEmptyPath ? (_nugetConfigFile = Root.AppendPart( "nuget.config" )) : _nugetConfigFile;

    internal NormalizedPath DirectoryPackageProps => _directoryPackageProps.IsEmptyPath ? (_directoryPackageProps = Root.AppendPart( "Directory.Packages.props" )) : _directoryPackageProps;

    internal NormalizedPath CKliPluginsFolder => _ckliPluginsFolder.IsEmptyPath ? (_ckliPluginsFolder = Root.AppendPart( "CKli.Plugins" )) : _ckliPluginsFolder;

    internal NormalizedPath CKliPluginsCSProj => _ckliPluginsCSProj.IsEmptyPath ? (_ckliPluginsCSProj = CKliPluginsFolder.AppendPart( "CKli.Plugins.csproj" )) : _ckliPluginsCSProj;

    internal NormalizedPath CKliPluginsFile => _ckliPluginsFile.IsEmptyPath ? (_ckliPluginsFile = CKliPluginsFolder.AppendPart( "CKli.Plugins.cs" )) : _ckliPluginsFile;

    internal NormalizedPath CKliCompiledPluginsFile => _ckliCompiledPluginsFile.IsEmptyPath ? (_ckliCompiledPluginsFile = CKliPluginsFolder.AppendPart( "CKli.CompiledPlugins.cs" )) : _ckliCompiledPluginsFile;

    internal IPluginCollection WorldPlugins => _worlPlugins;

    PluginMachinery( LocalWorldName worldName, WorldDefinitionFile definitionFile )
    {
        _definitionFile = definitionFile;
        _name = $"{worldName.StackName}-Plugins{worldName.LTSName}";
        _root = worldName.Stack.StackWorkingFolder.AppendPart( Name );
        _runFolder = worldName.Stack.StackWorkingFolder.Combine( $"$Local/{Name}/bin/CKli.Plugins/run" );
        _dllPath = _runFolder.AppendPart( "CKli.Plugins.dll" );
        _runFolderVNext = worldName.Stack.StackWorkingFolder.Combine( $"$Local/{Name}/bin/CKli.Plugins/vNext" );
        _dllPathVNext = _runFolderVNext.AppendPart( "CKli.Plugins.dll" );
    }

    internal static PluginMachinery? Create( IActivityMonitor monitor, LocalWorldName worldName, WorldDefinitionFile definitionFile )
    {
        Throw.DebugAssert( !definitionFile.IsPluginsDisabled && World.PluginLoader != null );
        var machinery = new PluginMachinery( worldName, definitionFile );
        bool forceRecompile = false;
        if( !Directory.Exists( machinery.Root ) )
        {
            machinery.CreateSolution( monitor );
            // Even if the DLLPath in $Local/ folder should not be here, we take no risk and
            // triggers a compilation.
            forceRecompile = true;
        }
        else
        {
            if( _versionChecked == null || !_versionChecked.Contains( machinery.Root ) )
            {
                if( !machinery.CheckCKliPluginsCoreVersion( monitor, out forceRecompile ) )
                {
                    return null;
                }
                _versionChecked ??= new HashSet<string>();
                _versionChecked.Add( machinery.Root );
            }
            // If we have a hook for the NuGet config file it must be applied now,
            // before any dotnet/nuget interaction.  
            if( _nuGetConfigFileHook != null && !ApplyNuGetConfigFileHook( monitor, machinery.NuGetConfigFile ) )
            {
                return null;
            }
        }
        // Before calling CheckCompiledPlugins that may moves vNext to the run folder, we must ensure
        // that there are no alive loaded plugins that would lock the run folder. 
        if( !RelaseCurrentSingleRunning( monitor ) )
        {
            return null;
        }

        // We are ready to compile if needed.
        if( !machinery.CheckCompiledPlugins( monitor, forceRecompile ) )
        {
            return null;
        }
        // If compilation succeeds, we read the <Plugins> configurations:
        // the PluginLoader binds each primary plugin to its configuration element.
        var pluginsConfiguration = definitionFile.ReadPluginsConfiguration( monitor );
        if( pluginsConfiguration == null )
        {
            return null;
        }
        var pluginContext = new PluginCollectorContext( worldName, pluginsConfiguration );
        var worldPlugins = World.PluginLoader( monitor, machinery.DllPath, pluginContext );
        if( worldPlugins == null )
        {
            return null;
        }
        // Memorizes the AssemblyLoadContext to be able to wait for its actual unload.
        _singleRunning = new WeakReference( worldPlugins, trackResurrection: true );
        //// Generating Compiled plugins version.
        //if( !worldPlugins.IsCompiledPlugins )
        //{
        //    File.WriteAllText( machinery.CKliCompiledPluginsFile, worldPlugins.GenerateCode() );
        //    worldPlugins.Dispose();

        //    if( !RelaseCurrentSingleRunning( monitor )
        //        || !machinery.CompilePlugins( monitor, vNext: false ) )
        //    {
        //        return null;
        //    }
        //    worldPlugins = World.PluginLoader( monitor, machinery.DllPath, pluginContext );
        //    if( worldPlugins == null )
        //    {
        //        return null;
        //    }
        //    _singleRunning = new WeakReference( worldPlugins, trackResurrection: true );
        //}
        // The plugins are available. They will be instantiated right after a World instance will be available.
        machinery._worlPlugins = worldPlugins;
        return machinery;
    }

    static bool RelaseCurrentSingleRunning( IActivityMonitor monitor )
    {
        if( _singleRunning != null && _singleRunning.IsAlive )
        {
            for( int i = 0; _singleRunning.IsAlive && (i < 10); i++ )
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            if( _singleRunning.IsAlive )
            {
                monitor.Error( $"""
                    Current plugins cannot be unloaded.
                    A Plugin is still referenced from the World. Plugins can be disabled to isolate the culprit.
                    CKli uses AssemblyLoadContext, see https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability.
                    """ );
                return false;
            }
        }
        _singleRunning = null;
        return true;
    }

    internal void ReleasePlugins()
    {
        // This is the CKli.Loader.PluginLoadContext.Dispose().
        // It disposes its own IPluginCollection obtained from the CKli.Plugins dll and initiates
        // its own Unload.
        _worlPlugins.Dispose();
        _worlPlugins = null;
    }

    bool CheckCKliPluginsCoreVersion( IActivityMonitor monitor, out bool mustRecompile )
    {
        mustRecompile = false;
        try
        {
            var d = XDocument.Load( DirectoryPackageProps );
            var ckliPluginsCore = d.Root?.Elements( "ItemGroup" )
                                         .Elements( "PackageVersion" )
                                         .FirstOrDefault( e => e.Attribute( "Include" )?.Value == "CKli.Plugins.Core" );
            if( ckliPluginsCore == null )
            {
                monitor.Error( $"Unable to find <PackageVersion Include=\"CKli.Plugins.Core\" Version=\"...\" /> in '{DirectoryPackageProps}'." );
                return false;
            }
            var v = SVersion.TryParse( ckliPluginsCore.Attribute( "Version" )?.Value );
            if( !v.IsValid )
            {
                monitor.Error( $"Invalid version in {ckliPluginsCore} (in '{DirectoryPackageProps}')." );
                return false;
            }
            var ckliVersion = World.CKliVersion.Version;
            Throw.Assert( ckliVersion != null );
            if( v == ckliVersion )
            {
                return true;
            }
            if( !_definitionFile.World.IsDefaultWorld )
            {
                monitor.Error( $"""
                               This world '{_definitionFile.World.FullName}' is a Long Term Support world.
                               It uses CKli in version '{v}'. This CKli version is '{ckliVersion}'.
                               Please use the appropriate CKli version.
                               """ );
                return false;
            }
            monitor.Info( $"""
                          Updating 'Directory.Package.Props' file:
                          {ckliPluginsCore}
                          To use Version="{ckliVersion}".
                          """ );
            ckliPluginsCore.SetAttributeValue( "Version", ckliVersion );
            d.Save( DirectoryPackageProps );
            mustRecompile = true;
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While checking CKli.Plugins.Core version in '{DirectoryPackageProps}'.", ex );
            return false;
        }
    }

    bool CheckCompiledPlugins( IActivityMonitor monitor, bool forceRecompile )
    {
        // Uses File's LastWriteTime:
        // When file doesn't exist, the LastWriteTime is 01/01/1601.
        // By using file time, we preserve any build manually done in the regular "run" folder
        // even if a previous change in the plugins triggered a next build in vNext.
        if( !forceRecompile && File.GetLastWriteTimeUtc( _dllPathVNext ) > File.GetLastWriteTimeUtc( _dllPath ) )
        {
            monitor.Trace( $"A new compiled version of the Plugins is available. Replacing '{_runFolder}'." );
            if( !FileHelper.DeleteFolder( monitor, _runFolder )
                || !FileHelper.TryMoveFolder( monitor, _runFolderVNext, _runFolder ) )
            {
                return false;
            }
        }
        else if( forceRecompile || !File.Exists( DllPath ) )
        {
            return CompilePlugins( monitor, vNext: false );
        }
        return true;
    }

    void CreateSolution( IActivityMonitor monitor )
    {
        monitor.Info( $"Creating '{Name}' solution." );
        Directory.CreateDirectory( Root );
        File.WriteAllText( SlnxPath, DefaultSlnFile );
        Directory.CreateDirectory( CKliPluginsFolder );
        File.WriteAllText( DirectoryBuildProps, string.Format( DefaultDirectoryBuildPropsPattern, Name ) );
        File.WriteAllText( DirectoryPackageProps, DefaultDirectoryPackageProps );
        File.WriteAllText( CKliPluginsCSProj, DefaultCKliPluginsCSProj );
        File.WriteAllText( CKliPluginsFile, DefaultCKliPluginsFile );
        File.WriteAllText( NuGetConfigFile, DefaultNuGetConfigFile );
        if( _nuGetConfigFileHook != null )
        {
            Throw.CheckState( ApplyNuGetConfigFileHook( monitor, NuGetConfigFile ) );
        }
    }

    bool CompilePlugins( IActivityMonitor monitor, bool vNext )
    {
        var args = new StringBuilder( "build ", 256 );
        args.Append( CKliPluginsCSProj.LastPart );
        args.Append( " --tl:off" );
        if( vNext )
        {
            args.Append( " /p:ArtifactsPivots=vNext" );
        }
        using var gLog = monitor.OpenTrace( $"Compiling '{CKliPluginsCSProj.LastPart}'{(vNext ? " in 'vNext'" : "")}." );
        int exitCode = ProcessRunner.RunProcess( monitor.ParallelLogger,
                                                 "dotnet",
                                                 args.ToString(),
                                                 CKliPluginsFolder,
                                                 environmentVariables: null );
        if( exitCode != 0 )
        {
            monitor.CloseGroup( $"Failed to build '{Name}' solution. Exit code = '{exitCode}'." );
            return false;
        }
        return true;
    }

    internal bool CreatePlugin( IActivityMonitor monitor, string shortPluginName, string fullPluginName )
    {
        using var gLog = monitor.OpenTrace( $"Creating plugin '{fullPluginName}'." );
        var projectPath = Root.AppendPart( fullPluginName );
        if( Directory.Exists( projectPath ) )
        {
            monitor.Error( $"Directory '{projectPath}' already exists." );
            return false;
        }
        // First, try to add the plugin registration to the central CKli.Plugins project.
        // If something fails, there is no side effect.
        var ckliPlugins = CKliPluginsProject.Create( monitor, this );
        if( ckliPlugins == null
            || !ckliPlugins.AddProjectReference( monitor, shortPluginName, fullPluginName ) 
            || !ckliPlugins.Save( monitor ) )
        {
            return false;
        }
        Directory.CreateDirectory( projectPath );
        File.WriteAllText( projectPath.AppendPart( $"{fullPluginName}.csproj" ), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
                <ItemGroup>
                    <PackageReference Include="CKli.Plugins.Core" />
                </ItemGroup>
            </Project>
            """ );
        File.WriteAllText( projectPath.AppendPart( $"{shortPluginName}Plugin.cs" ), $$"""
            using CK.Core;
            using CKli.Core;
            using System;
            using System.IO;
            using System.Linq;
            using System.Collections.Generic;

            namespace CKli.{{shortPluginName}}.Plugin;

            public sealed class {{shortPluginName}}Plugin : PluginBase
            {
                /// <summary>
                /// This is a primary plugin.
                /// </summary>
                public {{shortPluginName}}Plugin( IPrimaryPluginContext primaryContext )
                    : base( primaryContext )
                {
                    primaryContext.World.Events.PluginInfo += e =>
                    {
                        Throw.CheckState( PrimaryContext.PluginInfo.FullPluginName == "{{fullPluginName}}" );
                        Throw.CheckState( PrimaryContext.World == e.World );
                        e.AddMessage( PrimaryContext, b => b.Append( "Message from '{{shortPluginName}}' plugin." ) );
                        e.Monitor.Info( $"New '{{shortPluginName}}' in world '{e.World.Name}' plugin certainly requires some development." );
                        Console.WriteLine( $"Hello from '{{shortPluginName}}' plugin." );
                    };
                }
            }
            
            """ );
        // Uses dotnet tooling to add the project to the sln.
        // We don't care if it takes time here and this acts as a check.
        if( ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", $"sln add {fullPluginName}", Root, null ) != 0 )
        {
            monitor.Error( $"Command 'dotnet sln add {fullPluginName}' failed." );
            return false;
        }
        return CompilePlugins( monitor, vNext: true );
    }

    internal bool AddOrSetPluginPackage( IActivityMonitor monitor, string shortPluginName, string fullPluginName, SVersion version )
    {
        using var gLog = monitor.OpenTrace( $"Adding or updating plugin '{fullPluginName}' at version '{version}'." );
        var ckliPlugins = CKliPluginsProject.Create( monitor, this );
        if( ckliPlugins == null
            || !ckliPlugins.AddOrSetPackageReference( monitor, shortPluginName, fullPluginName, version, out bool added, out bool versionChanged )
            || !ckliPlugins.Save( monitor ) )
        {
            return false;
        }
        if( added )
        {
            // Uses dotnet tooling to add the project to the sln.
            // We don't care if it takes time here and this acts as a check.
            if( ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", $"sln add {fullPluginName}", Root, null ) != 0 )
            {
                monitor.Error( $"Command 'dotnet sln add {fullPluginName}' failed." );
                return false;
            }
        }
        if( !added && !versionChanged )
        {
            monitor.CloseGroup( "No change, skipping plugins recompilation." );
            return true;
        }
        return CompilePlugins( monitor, vNext: true );
    }

    internal bool RemovePlugin( IActivityMonitor monitor, string shortPluginName, string fullPluginName )
    {
        using var gLog = monitor.OpenTrace( $"Removing plugin '{fullPluginName}'." );
        var ckliPlugins = CKliPluginsProject.Create( monitor, this );
        if( ckliPlugins == null
            || !ckliPlugins.RemovePlugin( monitor, shortPluginName, fullPluginName, out var wasProjectReference )
            || !ckliPlugins.Save( monitor ) )
        {
            return false;
        }
        if( wasProjectReference )
        {
            var projectPath = Root.AppendPart( fullPluginName );
            if( Directory.Exists( projectPath ) )
            {
                if( !FileHelper.DeleteFolder( monitor, projectPath ) )
                {
                    return false;
                }
            }
        }
        if( ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", $"sln remove {fullPluginName}", Root, null ) != 0 )
        {
            monitor.Error( $"Command 'dotnet sln remove {fullPluginName}' failed." );
            return false;
        }
        return CompilePlugins( monitor, vNext: true );
    }

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

    static bool ApplyNuGetConfigFileHook( IActivityMonitor monitor, NormalizedPath nuGetConfigFile )
    {
        Throw.DebugAssert( _nuGetConfigFileHook != null );
        try
        {
            var d = XDocument.Load( nuGetConfigFile );
            _nuGetConfigFileHook( monitor, d );
            d.Save( nuGetConfigFile );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "While calling NuGetConfigFileHook.", ex );
            return false;
        }
    }

    public static bool EnsureFullPluginName( IActivityMonitor monitor,
                                             string? pluginName,
                                             [NotNullWhen( true )] out string? shortPluginName,
                                             [NotNullWhen( true )] out string? fullPluginName )
    {
        if( !EnsureFullPluginName( pluginName, out shortPluginName, out fullPluginName ) )
        {
            monitor.Error( $"Invalid plugin name '{pluginName}'. Must be '[A-Za-z][A-Za-z0-9]' or 'Ckli.[A-Za-z][A-Za-z0-9].Plugin' (and not \"global\")." );
            return false;
        }
        return true;
    }

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
    /// Gets whether this name is a valid "CKli.XXX.Plugin" name (but not "CKli.global.Plugin").
    /// </summary>
    /// <param name="pluginName">The name to test.</param>
    /// <returns>True if this is a valid full plugin name.</returns>
    public static bool IsValidFullPluginName( [NotNullWhen(true)] string? pluginName )
    {
        return pluginName != null
               && ValidFullPluginName().Match( pluginName ).Success
               && !pluginName.Equals( "CKli.global.Plugin", StringComparison.OrdinalIgnoreCase )
;
    }

    /// <summary>
    /// Gets whether this name is a valid short "XXX" plugin name: starts
    /// with [A-Za-z] followed by at least 2 [A-Za-z0-9] and is not "global".
    /// </summary>
    /// <param name="pluginName">The name to test.</param>
    /// <returns>True if this is a valid short plugin name.</returns>
    public static bool IsValidShortPluginName( [NotNullWhen( true )] string? pluginName )
    {
        return pluginName != null
               && !pluginName.Equals( "global", StringComparison.OrdinalIgnoreCase )
               && ValidShortPluginName().Match( pluginName ).Success;
    }

    [GeneratedRegex( @"^CKli\.[A-Za-z][A-Za-z0-9]{2,}\.Plugin$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex ValidFullPluginName();

    [GeneratedRegex( @"^[A-Za-z][A-Za-z0-9]{2,}$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
    private static partial Regex ValidShortPluginName();

#pragma warning disable CA2211 // Non-constant fields should not be visible

    public static string DefaultSlnFile = """
                <Solution>
                  <Configurations>
                    <Platform Name="Any CPU" />
                  </Configurations>
                  <Project Path="CKli.Plugins/CKli.Plugins.csproj" />
                </Solution>
                """;

    /// <summary>
    /// Gets the default "CKli.Plugins/CKli.Plugins.csproj" file content.
    /// </summary>
    public static string DefaultCKliPluginsCSProj = """
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="CKli.Plugins.Core" />
                  </ItemGroup>

                </Project>
                
                """;

    public static string DefaultNuGetConfigFile = """
                <configuration>

                  <packageSources>
                    <clear />

                    <add key="Signature-OpenSource" value="https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json" />
                    <add key="NuGet" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>

                  <!-- Required by Central Package Management -->
                  <packageSourceMapping>
                    <packageSource key="NuGet">
                      <package pattern="*" />
                    </packageSource>
                    <packageSource key="Signature-OpenSource">
                      <package pattern="*" />
                    </packageSource>
                  </packageSourceMapping>

                </configuration>                
                """;
    /// <summary>
    /// Gets the default "CKli.Plugins/Plugins.cs" file content.
    /// </summary>
    public static string DefaultCKliPluginsFile = """
                using CKli.Core;

                namespace CKli.Plugins;

                public static class Plugins
                {
                    public static IPluginCollection Register( PluginCollectorContext ctx )
                    {
                        return PluginCollector.Create( ctx ).BuildPluginCollection( [
                            // <AutoSection>
                            // </AutoSection>
                        ] );
                    }
                }
                                
                """;

    /// <summary>
    /// Gets the default "Directory.Package.props" file content.
    /// </summary>
    public static string DefaultDirectoryPackageProps = $"""
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageVersion Include="CKli.Plugins.Core" Version="{World.CKliVersion.Version}" />
                  </ItemGroup>
                </Project>
                
                """;

    /// <summary>
    /// Gets the default "Directory.Build.props" file content: the {0} placeholder is for the <see cref="Name"/>.
    /// </summary>
    public static string DefaultDirectoryBuildPropsPattern = """
                <Project>
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ArtifactsPath>$(MSBuildThisFileDirectory)../$Local/{0}</ArtifactsPath>
                    <ArtifactsPivots>run</ArtifactsPivots>
                  </PropertyGroup>
                </Project>
                
                """;

#pragma warning restore CA2211 // Non-constant fields should not be visible

}

