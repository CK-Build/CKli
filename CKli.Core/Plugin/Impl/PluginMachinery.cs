using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Supports the Plugin infrastructure, not intended to be used directly.
/// <para>
/// This class handles plugins discovery and compilation. It holds and control the lifetime of
/// the reference to the initialized <see cref="IPluginFactory"/>.
/// </para>
/// </summary>
public sealed partial class PluginMachinery
{
    readonly string _name;
    readonly NormalizedPath _root;
    readonly NormalizedPath _runFolder;
    readonly NormalizedPath _dllPath;
    readonly WorldDefinitionFile _definitionFile;
    NormalizedPath _slnxPath;
    NormalizedPath _ckliPluginsFolder;
    NormalizedPath _directoryBuildProps;
    NormalizedPath _directoryPackageProps;
    NormalizedPath _nugetConfigFile;
    NormalizedPath _ckliPluginsCSProj;
    NormalizedPath _ckliPluginsFile;
    NormalizedPath _ckliCompiledPluginsFile;
    NormalizedPath _pluginTestsCSProjFilePath;

    // Fundamental singleton!
    // This MAY be transformed in a dictionary per World (key would be the RunFolder) to allow more than a
    // World to exist at the same time in the process...
    // But this would bring more complexities for no real benefits: CKli is currently a World tool
    // not a server for multiple Worlds.
    static WeakReference? _singleFactory;

    // Caches whether "Directory.Packages.props" contains the <PackageVersion Include="CKli.Plugins.Core" Version="..." />
    // that is World.CKliVersion.Version. This check is done only once.
    static HashSet<string>? _versionChecked;

    static Action<IActivityMonitor, XDocument>? _nuGetConfigFileHook;

    // Result, on success, is set by Create (on error, the failing Machinery is not exposed).
    //
    // In OnPluginChanged, we first release the existing world plugins and call World.AcquirePlugins
    // only if we were able to reload the plugin factory.
    //
    [AllowNull] IPluginFactory _pluginFactory;

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

    internal NormalizedPath PluginTestsCSProjFilePath => _pluginTestsCSProjFilePath.IsEmptyPath ? (_pluginTestsCSProjFilePath = Root.Combine( "Tests/Plugins.Tests/Plugins.Tests.csproj" )) : _pluginTestsCSProjFilePath;

    internal IPluginFactory PluginFactory => _pluginFactory;

    internal PluginMachinery( LocalWorldName worldName, WorldDefinitionFile definitionFile )
    {
        _definitionFile = definitionFile;

        _name = GetPluginSolutionName( worldName );
        _runFolder = worldName.Stack.StackWorkingFolder.Combine( GetLocalRunFolder( _name ) );
        _root = worldName.Stack.StackWorkingFolder.AppendPart( _name );
        _dllPath = _runFolder.AppendPart( "CKli.Plugins.dll" );
    }

    internal void Initialize( IActivityMonitor monitor )
    {
        // When the first load failed, if it is a recoverable error, we have a context to
        // try a recompile.
        if( !FirstLoad( monitor, out PluginCollectorContext? toRecompile )
            && (toRecompile == null || !RecompileAndLoad( monitor, toRecompile )) )
        {
            // No way... Switch to the NoPluginFactory.
            monitor.Warn( "Unable to load plugins. Working without plugins." );
            _pluginFactory = GetNoPluginFactory();
        }
    }

    // First load.
    bool FirstLoad( IActivityMonitor monitor, out PluginCollectorContext? toRecompile )
    {
        Throw.DebugAssert( World.PluginLoader != null );
        toRecompile = null;
        bool preCompile = false;
        if( !Directory.Exists( Root ) )
        {
            CreateSolution( monitor );
            // Even if the DLLPath in $Local/ folder should not be here, we take no risk and
            // triggers a compilation.
            preCompile = true;
        }
        else
        {
            if( _versionChecked == null || !_versionChecked.Contains( Root ) )
            {
                if( !CheckCKliPluginsCoreVersion( monitor, out preCompile ) )
                {
                    return false;
                }
                _versionChecked ??= new HashSet<string>();
                _versionChecked.Add( Root );
            }
            // If we have a hook for the NuGet config file it must be applied now,
            // before any dotnet/nuget interaction.  
            if( _nuGetConfigFileHook != null && !ApplyNuGetConfigFileHook( monitor, NuGetConfigFile ) )
            {
                return false;
            }
        }
        return LoadPluginFactory( monitor, preCompile, out toRecompile );
    }

    [MethodImpl( MethodImplOptions.NoInlining )]
    bool RecompileAndLoad( IActivityMonitor monitor, PluginCollectorContext pluginContext )
    {
        Throw.DebugAssert( World.PluginLoader != null );
        using( monitor.OpenTrace( $"Recompiling CKli.Plugins and loading Plugin factory." ) )
        {
            if( !ReleaseCurrentSingleRunning( monitor )
                || !DoCompilePlugins( monitor ) )
            {
                return false;
            }
            var pluginFactory = World.PluginLoader( monitor, DllPath, pluginContext, out _, out _singleFactory );
            if( pluginFactory == null )
            {
                return false;
            }
            _pluginFactory = pluginFactory;
            return true;
        }
    }

    [MethodImpl( MethodImplOptions.NoInlining )]
    bool LoadPluginFactory( IActivityMonitor monitor, bool preCompile, out PluginCollectorContext? toRecompile )
    {
        Throw.DebugAssert( World.PluginLoader != null );
        toRecompile = null;
        // Obtains the <Plugins> configurations. It is read for the first load only (it is cached).
        // The PluginLoader binds each primary plugin to its configuration element.
        var pluginsConfiguration = _definitionFile.ReadPluginsConfiguration( monitor );
        if( pluginsConfiguration == null )
        {
            return false;
        }
        // There must be no alive loaded plugins now. 
        if( !ReleaseCurrentSingleRunning( monitor ) )
        {
            return false;
        }
        // First compilation if needed.
        if( preCompile && !DoCompilePlugins( monitor ) )
        {
            return false;
        }
        // Loads the plugins.
        var pluginContext = new PluginCollectorContext( _definitionFile.World, pluginsConfiguration );
        var pluginFactory = World.PluginLoader( monitor, DllPath, pluginContext, out bool recoverableError, out _singleFactory );
        if( pluginFactory == null )
        {
            if( !recoverableError )
            {
                return false;
            }
            if( File.Exists( CKliCompiledPluginsFile ) )
            {
                monitor.Trace( $"Deleting '{CKliCompiledPluginsFile}'." );
                FileHelper.DeleteFile( monitor, CKliCompiledPluginsFile );
            }
            toRecompile = pluginContext;
            return false;
        }
        // Decide whether a recompilation is required. 
        if( pluginFactory.CompileMode != _definitionFile.CompileMode )
        {
            if( _definitionFile.CompileMode == PluginCompileMode.None )
            {
                monitor.Trace( """
                    Deleting Compiled plugins file (CompileMode = None).
                    CKli.Plugins will be recompiled without compiled plugins. Only reflection will be used for subsequent loads.
                    """ );
                if( !FileHelper.DeleteFile( monitor, CKliCompiledPluginsFile ) )
                {
                    return false;
                }
            }
            else
            {
                if( pluginFactory.CompileMode == PluginCompileMode.None )
                {
                    monitor.Trace( $"""
                        Generating Compiled plugins file (CompileMode = {_definitionFile.CompileMode}).
                        CKli.Plugins will be recompiled with compiled plugins.
                        """ );
                    File.WriteAllText( CKliCompiledPluginsFile, pluginFactory.GenerateCode() );
                }
                else
                {
                    monitor.Trace( $"""
                        CKli.Plugins is compiled in '{pluginFactory.CompileMode}'.
                        Recompiling it in '{_definitionFile.CompileMode}'.
                        """ );
                }
            }
            pluginFactory.Dispose();
            pluginFactory = null;
            toRecompile = pluginContext;
        }
        // The factory is available (unless we must recompile).
        _pluginFactory = pluginFactory;
        return pluginFactory != null;
    }

    static bool ReleaseCurrentSingleRunning( IActivityMonitor monitor )
    {
        if( _singleFactory != null && _singleFactory.IsAlive )
        {
            for( int i = 0; _singleFactory.IsAlive && (i < 10); i++ )
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            if( _singleFactory.IsAlive )
            {
                monitor.Error( $"""
                    Current plugins cannot be unloaded.
                    A Plugin is still referenced from the World. Plugins can be disabled to isolate the culprit.
                    CKli uses AssemblyLoadContext, see https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability.
                    """ );
                return false;
            }
        }
        _singleFactory = null;
        return true;
    }

    internal void ReleasePluginFactory()
    {
        // This is the CKli.Loader.PluginLoadContext.Dispose().
        // It disposes its own IPluginFactory obtained from the CKli.Plugins dll and initiates
        // its own Unload.
        // If the current factory is the NoPluginFactory, we do nothing: we keep
        // the _pluginFactory as-is: it will be replaced by an actual factory if a reload
        // succeeds.
        if( _pluginFactory != null && _pluginFactory != _noPluginFactory )
        {
            _pluginFactory.Dispose();
            _pluginFactory = null;
        }
    }

    bool CheckCKliPluginsCoreVersion( IActivityMonitor monitor, out bool mustRecompile )
    {
        mustRecompile = false;
        try
        {
            var d = XDocument.Load( DirectoryPackageProps, LoadOptions.PreserveWhitespace );
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
                monitor.Error( $"Invalid version in {ckliPluginsCore} (in '{DirectoryPackageProps}'): {v.ErrorMessage}." );
                return false;
            }
            var ckliVersion = World.CKliVersion.Version;
            Throw.Assert( ckliVersion != null );
            if( v != ckliVersion )
            {
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
                              Updating 'Directory.Package.props' file:
                              {ckliPluginsCore}
                              To use Version="{ckliVersion}".
                              """ );
                ckliPluginsCore.SetAttributeValue( "Version", ckliVersion );
                d.SaveWithoutXmlDeclaration( DirectoryPackageProps );
                mustRecompile = true;
            }
            // Handling CKli.Testing version in 'Tests/Plugins.Tests/Plugins.Tests.csproj' if it exists.
            // We do this even if the version in the Directory.Package.props was okay and if CKli.Testing
            // is not referenced by the test project, we just emit a warning.
            if( File.Exists( PluginTestsCSProjFilePath ) )
            {
                var testsCSProj = XDocument.Load( PluginTestsCSProjFilePath, LoadOptions.PreserveWhitespace );
                var ckliTesting = testsCSProj.Root?.Elements( "ItemGroup" )
                                             .Elements( "PackageReference" )
                                             .FirstOrDefault( e => e.Attribute( "Include" )?.Value == "CKli.Testing" );
                if( ckliTesting == null )
                {
                    monitor.Warn( """
                        Unable to find <PackageReference Include="CKli.Testing" Version="..." /> element in 'Tests/Plugins.Tests/Plugins.Tests.csproj'.
                        Skipping CKli version update.
                        """ );
                }
                else
                {
                    v = SVersion.TryParse( ckliTesting.Attribute( "Version" )?.Value );
                    if( !v.IsValid )
                    {
                        monitor.Error( $"Invalid version in {ckliTesting} (in '{PluginTestsCSProjFilePath}'): {v.ErrorMessage}." );
                        return false;
                    }
                    if( v != ckliVersion )
                    {
                        monitor.Info( $"""
                          Updating 'Tests/Plugins.Tests/Plugins.Tests.csproj' file:
                          {ckliTesting}
                          To use Version="{ckliVersion}".
                          """ );
                        ckliTesting.SetAttributeValue( "Version", ckliVersion );
                        testsCSProj.SaveWithoutXmlDeclaration( PluginTestsCSProjFilePath );
                        mustRecompile = true;
                    }
                }
            }
            if( mustRecompile )
            {
                monitor.Trace( $"Deleting '{CKliCompiledPluginsFile.LastPart}'." );
                if( !FileHelper.DeleteFile( monitor, CKliCompiledPluginsFile ) )
                {
                    return false;
                }
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While checking CKli.Plugins.Core version in '{DirectoryPackageProps}'.", ex );
            return false;
        }
    }

    void CreateSolution( IActivityMonitor monitor )
    {
        using( monitor.OpenInfo( $"Creating '{Name}' solution." ) )
        {
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
    }

    bool DoCompilePlugins( IActivityMonitor monitor )
    {
        var args = new StringBuilder( "build ", 256 );
        args.Append( CKliPluginsCSProj.LastPart );
        args.Append( " --tl:off --nologo" );
        args.Append( " -c " ).Append( _definitionFile.CompileMode == PluginCompileMode.Debug ? "Debug" : "Release" );
        using var gLog = monitor.OpenTrace( $"""
            Compiling '{CKliPluginsCSProj.LastPart}'
            dotnet {args}.
            """ );
        int? exitCode = ProcessRunner.RunProcess( monitor.ParallelLogger,
                                                  "dotnet",
                                                  args.ToString(),
                                                  CKliPluginsFolder );
        if( exitCode != 0 )
        {
            monitor.CloseGroup( $"Failed to build '{Name}' solution. Exit code = '{exitCode}'." );
            return false;
        }
        return true;
    }

    internal bool SetPluginCompileMode( IActivityMonitor monitor, World world, PluginCompileMode mode )
    {
        Throw.DebugAssert( mode != _definitionFile.CompileMode );
        _definitionFile.SetPluginCompileMode( monitor, mode );
        return OnPluginChanged( monitor, world, false, true );
    }

    internal bool ForceRecompilePlugins( IActivityMonitor monitor, World world )
    {
        monitor.Info( "Forcing plugin recompilation." );
        return OnPluginChanged( monitor, world, updateCompiledPlugins: true, reloadPlugins: true );
    }

    internal bool CreatePlugin( IActivityMonitor monitor, World world, string shortPluginName, string fullPluginName )
    {
        using var gLog = monitor.OpenTrace( $"Creating plugin '{fullPluginName}'." );
        var projectPath = Root.AppendPart( fullPluginName );
        var projectCSProjPath = projectPath.AppendPart( $"{fullPluginName}.csproj" );
        var projectPrimaryPluginPath = projectPath.AppendPart( $"{shortPluginName}Plugin.cs" );

        bool alreadyHere = false;
        if( Directory.Exists( projectPath ) )
        {
            bool hasProject = File.Exists( projectCSProjPath );
            bool hasPlugin = File.Exists( projectPrimaryPluginPath );
            if( !hasProject || !hasPlugin )
            {
                using( monitor.OpenError( $"Directory '{projectPath}' already exists, but..." ) )
                {
                    if( !hasProject ) monitor.Error( $"Expecting existing '{projectCSProjPath.LastPart}' file." );
                    if( !hasPlugin ) monitor.Error( $"Expecting existing '{projectPrimaryPluginPath.LastPart}' file." );
                }
                return false;
            }
            monitor.Trace( $"""
                Directory '{projectPath}', '{projectCSProjPath.LastPart}' and '{projectPrimaryPluginPath.LastPart}' files already exists.
                Considering that the plugin project has already been created.
                """ );
            alreadyHere = true;
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
        _definitionFile.EnsurePluginConfiguration( monitor, shortPluginName );
        if( !alreadyHere )
        {
            Directory.CreateDirectory( projectPath );
            File.WriteAllText( projectCSProjPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <ItemGroup>
                        <PackageReference Include="CKli.Plugins.Core" />
                    </ItemGroup>
                </Project>
                """ );
            File.WriteAllText( projectPrimaryPluginPath, $$"""
                using CK.Core;
                using CKli.Core;
                using System;
                using System.IO;
                using System.Linq;
                using System.Collections.Generic;

                namespace CKli.{{shortPluginName}}.Plugin;

                public sealed class {{shortPluginName}}Plugin : PrimaryPluginBase
                {
                    /// <summary>
                    /// This is a primary plugin. <see cref="PrimaryPluginBase.PrimaryPluginContext"/>
                    /// is always available (as well as the <see cref="PluginBase.World"/>).
                    /// </summary>
                    public {{shortPluginName}}Plugin( PrimaryPluginContext primaryContext )
                        : base( primaryContext )
                    {
                        primaryContext.World.Events.PluginInfo += e =>
                        {
                            Throw.CheckState( PrimaryPluginContext.PluginInfo.FullPluginName == "{{fullPluginName}}" );
                            Throw.CheckState( World == e.World );
                            Throw.CheckState( PrimaryPluginContext.World == e.World );
                            e.AddMessage( PrimaryPluginContext, e.ScreenType.Text( "Message from '{{shortPluginName}}' plugin." ) );
                            e.Monitor.Info( $"New '{{shortPluginName}}' in world '{e.World.Name}' plugin certainly requires some development." );
                            Console.WriteLine( $"Hello from '{{shortPluginName}}' plugin." );
                        };
                    }
                }
            
                """ );
        }
        // Uses dotnet tooling to add the project to the sln: this doesn't fail (ExitCode is 0) if the project is
        // already in the .sln or .slnx (and this is fine).
        // We don't care if it takes time here and this acts as a check.
        if( ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", $"sln add {fullPluginName}", Root, null ) != 0 )
        {
            monitor.Error( $"Command 'dotnet sln add {fullPluginName}' failed." );
            return false;
        }
        return OnPluginChanged( monitor, world, true, false );
    }

    internal bool AddOrSetPluginPackage( IActivityMonitor monitor,
                                         World world,
                                         string shortPluginName,
                                         string fullPluginName,
                                         SVersion version,
                                         out bool added,
                                         out bool versionChanged )
    {
        using var gLog = monitor.OpenTrace( $"Adding or updating plugin '{fullPluginName}' at version '{version}'." );
        var ckliPlugins = CKliPluginsProject.Create( monitor, this );
        if( ckliPlugins == null
            || !ckliPlugins.AddOrSetPackageReference( monitor, shortPluginName, fullPluginName, version, out added, out versionChanged )
            || !ckliPlugins.Save( monitor ) )
        {
            added = false;
            versionChanged = false;
            return false;
        }
        if( !added && !versionChanged )
        {
            monitor.CloseGroup( "No change, skipping plugins recompilation." );
            return true;
        }
        _definitionFile.EnsurePluginConfiguration( monitor, shortPluginName );
        return OnPluginChanged( monitor, world, true, true );
    }

    internal bool RemovePlugin( IActivityMonitor monitor, World world, string shortPluginName, string fullPluginName )
    {
        using var gLog = monitor.OpenTrace( $"Removing plugin '{fullPluginName}'." );
        var ckliPlugins = CKliPluginsProject.Create( monitor, this );
        if( ckliPlugins == null
            || !ckliPlugins.RemovePlugin( monitor, shortPluginName, fullPluginName, out var wasProjectReference )
            || !ckliPlugins.Save( monitor ) )
        {
            return false;
        }
        _definitionFile.RemovePluginConfiguration( monitor, shortPluginName );
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
            if( ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", $"sln remove {fullPluginName}/{fullPluginName}.csproj", Root, null ) != 0 )
            {
                monitor.Error( $"Command 'dotnet sln remove {fullPluginName}' failed." );
                return false;
            }
        }
        return OnPluginChanged( monitor, world, true, true );
    }

    bool OnPluginChanged( IActivityMonitor monitor, World world, bool updateCompiledPlugins, bool reloadPlugins )
    {
        world.ReleasePlugins();
        if( updateCompiledPlugins && !FileHelper.DeleteFile( monitor, CKliCompiledPluginsFile ) )
        {
            return false;
        }
        // When plugins change, the 4 possible reasons are:
        // - SetPluginCompileMode: This doesn't change anything (at least should not).
        //                         There should be no reason to reload the plugin instances except that the "ckli plugin --compile-mode"
        //                         displays the plugin infos after the mode change.
        //                         => reloadPlugins is true.
        //
        // - CreatePlugin: The new plugin does nothing (it doesn't touch its empty configuration element)
        //                 and necessarily works. There's no reason to reload the plugin instances.
        //                 => reloadPlugins is false.
        //
        // - RemovePlugin: The plugin and its configuration is removed. There is no "OnRemove" on a plugin.
        //                 If the removed plugin was referenced by another one, the LoadPluginFactory above
        //                 fails to load or recompile the plugins.
        //                 A plugin that loses an optional dependency MAY change something in its Xml configuration:
        //                 ==> reloadPlugins is true.
        //
        // - AddOrSetPluginPackage: Obviously, a Plugin may initialize its configuration.
        //                          ==> reloadPlugins is true.
        // 
        // Note that both CreatePlugin, RemovePlugin and AddOrSetPluginPackage are "transacted" (by a pre commit and
        // a reset hard on failure).
        //
        // When reloadPlugins is true, we instantiate and initialize the plugins even if we won't use them: this is to ensure
        // that everything works fine and to call Initialize() method that may update the Plugin configuration in
        // the definition file.
        // This is a "useless" operation (because the plugins won't be used in this run) that costs but it's not every day that
        // a plugin is added, removed or updated.
        //
        if( !LoadPluginFactory( monitor, updateCompiledPlugins, out PluginCollectorContext? toRecompile )
            && (toRecompile == null || !RecompileAndLoad( monitor, toRecompile )) )
        {
            return false;
        }
        return !reloadPlugins || world.AcquirePlugins( monitor );
    }

    static bool ApplyNuGetConfigFileHook( IActivityMonitor monitor, NormalizedPath nuGetConfigFile )
    {
        Throw.DebugAssert( _nuGetConfigFileHook != null );
        try
        {
            var d = XDocument.Load( nuGetConfigFile, LoadOptions.PreserveWhitespace );
            _nuGetConfigFileHook( monitor, d );
            d.SaveWithoutXmlDeclaration( nuGetConfigFile );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "While calling NuGetConfigFileHook.", ex );
            return false;
        }
    }


#pragma warning disable IDE1006 // Naming Styles
    const string DefaultSlnFile = """
                <Solution>
                  <Folder Name="/Solution Items/">
                    <File Path="Directory.Build.props" />
                    <File Path="Directory.Packages.props" />
                    <File Path="nuget.config" />
                  </Folder>
                  <Project Path="CKli.Plugins/CKli.Plugins.csproj" />
                </Solution>
                """;

    const string DefaultCKliPluginsCSProj = """
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="CKli.Plugins.Core" />
                  </ItemGroup>

                </Project>
                
                """;

    const string DefaultNuGetConfigFile = """
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

    const string DefaultCKliPluginsFile = """
                using CKli.Core;

                namespace CKli.Plugins;

                public static class Plugins
                {
                    public static IPluginFactory Register( PluginCollectorContext ctx )
                    {
                        return PluginCollector.Create( ctx ).BuildPluginFactory( [
                            // <AutoSection>
                            // </AutoSection>
                        ] );
                    }
                }
                                
                """;

    static readonly string DefaultDirectoryPackageProps = $"""
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
    const string DefaultDirectoryBuildPropsPattern = """
                <Project>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ArtifactsPath>$(MSBuildThisFileDirectory)../$Local/{0}</ArtifactsPath>
                    <ArtifactsPivots>run</ArtifactsPivots>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                
                """;

#pragma warning restore IDE1006 // Naming Styles

}

