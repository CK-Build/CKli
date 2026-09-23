using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
    /// <summary>
    /// Gets the "CKli.Plugins" folder name.
    /// </summary>
    public const string CKliPluginsFolderName = "CKli.Plugins";

    readonly string _name;
    readonly NormalizedPath _root;
    readonly NormalizedPath _runFolder;
    readonly NormalizedPath _dllPath;
    readonly WorldDefinitionFile _definitionFile;
    NormalizedPath _slnxPath;
    NormalizedPath _ckliPluginsFolder;
    NormalizedPath _directoryBuildProps;
    NormalizedPath _directoryPackageProps;
    NormalizedPath _ckliVersionProps;
    NormalizedPath _nugetConfigFile;
    NormalizedPath _ckliPluginsCSProj;
    NormalizedPath _ckliPluginsFile;
    NormalizedPath _ckliCompiledPluginsFile;
    NormalizedPath _pluginTestsCSProjFilePath;
    NormalizedPath _pluginTestsDirectoryBuildProps;
    // The last created PluginCollectorContext. It is immutable: it is reused to reload the plugins
    // (see RecoverFromInstantiationError).
    PluginCollectorContext? _pluginContext;

    // Fundamental singleton!
    // This MAY be transformed in a dictionary per World (key would be the RunFolder) to allow more than a
    // World to exist at the same time in the process...
    // But this would bring more complexities for no real benefits: CKli is currently a World tool
    // not a server for multiple Worlds.
    static WeakReference? _singleFactory;

    // Caches the Roots whose "Directory.Packages.props" (and optional "Tests/Plugins.Tests") have been migrated
    // to the $(CKliVersion) property. This one-time migration is attempted only once per process.
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
    /// Computed by <see cref="GetPluginSolutionName(WorldName)"/>.
    /// </summary>
    public string Name => _name;

    internal NormalizedPath Root => _root;

    internal NormalizedPath RunFolder => _runFolder;

    internal NormalizedPath DllPath => _dllPath;

    internal NormalizedPath SlnxPath => _slnxPath.IsEmptyPath ? (_slnxPath = Root.AppendPart( $"{Name}.slnx" )) : _slnxPath;

    internal NormalizedPath DirectoryBuildProps => _directoryBuildProps.IsEmptyPath ? (_directoryBuildProps = Root.AppendPart( "Directory.Build.props" )) : _directoryBuildProps;

    internal NormalizedPath NuGetConfigFile => _nugetConfigFile.IsEmptyPath ? (_nugetConfigFile = Root.AppendPart( "nuget.config" )) : _nugetConfigFile;

    internal NormalizedPath DirectoryPackageProps => _directoryPackageProps.IsEmptyPath ? (_directoryPackageProps = Root.AppendPart( "Directory.Packages.props" )) : _directoryPackageProps;

    /// <summary>
    /// The generated, git ignored "CKli.Version.props" file: it carries the $(CKliVersion) property that
    /// "Directory.Packages.props" and "Tests/Plugins.Tests/Plugins.Tests.csproj" reference.
    /// <para>
    /// This file is what keeps the CKli version out of the Stack repository: it is per developer, so 2
    /// developers on 2 CKli versions no longer fight over a tracked file (which used to leave the Stack
    /// dirty and break the next "ckli pull").
    /// </para>
    /// </summary>
    internal NormalizedPath CKliVersionProps => _ckliVersionProps.IsEmptyPath ? (_ckliVersionProps = Root.AppendPart( CKliVersionPropsFileName )) : _ckliVersionProps;

    /// <summary>
    /// The CKli version the plugins of this world must be built against: the world's
    /// <see cref="WorldDefinitionFile.PinnedCKliVersion"/> when it has one (a LTS world), the running
    /// CKli's <see cref="World.CKliVersion"/> otherwise.
    /// </summary>
    internal SVersion EffectiveCKliVersion
    {
        get
        {
            var v = _definitionFile.PinnedCKliVersion ?? World.CKliVersion.Version;
            // World.CKliVersion is read from this assembly's InformationalVersion: a malformed one
            // would silently produce a Version="" in the generated props and an obscure NuGet error.
            Throw.CheckState( "CKli's own assembly version must be valid.", v != null );
            return v;
        }
    }

    internal NormalizedPath CKliPluginsFolder => _ckliPluginsFolder.IsEmptyPath ? (_ckliPluginsFolder = Root.AppendPart( CKliPluginsFolderName )) : _ckliPluginsFolder;

    internal NormalizedPath CKliPluginsCSProj => _ckliPluginsCSProj.IsEmptyPath ? (_ckliPluginsCSProj = CKliPluginsFolder.AppendPart( "CKli.Plugins.csproj" )) : _ckliPluginsCSProj;

    internal NormalizedPath CKliPluginsFile => _ckliPluginsFile.IsEmptyPath ? (_ckliPluginsFile = CKliPluginsFolder.AppendPart( "CKli.Plugins.cs" )) : _ckliPluginsFile;

    internal NormalizedPath CKliCompiledPluginsFile => _ckliCompiledPluginsFile.IsEmptyPath ? (_ckliCompiledPluginsFile = CKliPluginsFolder.AppendPart( "CKli.CompiledPlugins.cs" )) : _ckliCompiledPluginsFile;

    internal NormalizedPath PluginTestsCSProjFilePath => _pluginTestsCSProjFilePath.IsEmptyPath ? (_pluginTestsCSProjFilePath = Root.Combine( "Tests/Plugins.Tests/Plugins.Tests.csproj" )) : _pluginTestsCSProjFilePath;

    // MSBuild stops walking up at the first "Directory.Build.props" it finds and the Plugins.Tests project has
    // its own (it works around https://github.com/dotnet/sdk/issues/45953), so the $(CKliVersion) defined beside
    // the plugin solution does NOT reach it: this companion must import the generated file itself.
    internal NormalizedPath PluginTestsDirectoryBuildProps => _pluginTestsDirectoryBuildProps.IsEmptyPath ? (_pluginTestsDirectoryBuildProps = Root.Combine( "Tests/Plugins.Tests/Directory.Build.props" )) : _pluginTestsDirectoryBuildProps;

    internal IPluginFactory PluginFactory => _pluginFactory;

    internal PluginMachinery( LocalWorldName worldName, WorldDefinitionFile definitionFile )
    {
        _definitionFile = definitionFile;

        _name = GetPluginSolutionName( worldName );
        _root = worldName.SharedDataFolder.AppendPart( _name );
        _runFolder = worldName.LocalDataFolder.Combine( GetLocalRunFolder( _name ) );
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
            _pluginFactory = GetOnErrorPluginFactory();
        }
    }

    // First load.
    bool FirstLoad( IActivityMonitor monitor, out PluginCollectorContext? toRecompile )
    {
        Throw.DebugAssert( World.PluginLoader != null );
        toRecompile = null;
        if( !Directory.Exists( Root ) )
        {
            using( monitor.OpenInfo( $"Creating '{Name}' solution." ) )
            {
                Directory.CreateDirectory( Root );
                File.WriteAllText( SlnxPath, DefaultSlnFile );
                Directory.CreateDirectory( CKliPluginsFolder );
                File.WriteAllText( DirectoryBuildProps, string.Format( DefaultDirectoryBuildPropsPattern, GetArtifactsPath( _definitionFile.World ) ) );
                File.WriteAllText( DirectoryPackageProps, DefaultDirectoryPackageProps );
                File.WriteAllText( CKliPluginsCSProj, DefaultCKliPluginsCSProj );
                File.WriteAllText( CKliPluginsFile, DefaultCKliPluginsFile );
                File.WriteAllText( NuGetConfigFile, DefaultNuGetConfigFile );
                // The generated "CKli.Version.props" is what gives a value to the $(CKliVersion) that the
                // "Directory.Packages.props" just written references: without it nothing restores.
                if( !EnsureCKliVersionProps( monitor, out _ ) )
                {
                    return false;
                }
                if( _nuGetConfigFileHook != null )
                {
                    Throw.CheckState( ApplyNuGetConfigFileHook( monitor, NuGetConfigFile ) );
                }
                // Even if the DLLPath in $Local/ folder should not be here, we take no risk and
                // triggers a compilation of the empty CKli-Plugins solution.
                return LoadPluginFactory( monitor, preCompile: true, out toRecompile );
            }
        }
        // "CKli.Version.props" is the stamp of the CKli version the plugins were last built against: writing it
        // is what asks for a recompilation. Nothing in the Stack repository moves here (the file is git ignored),
        // which is the whole point: the plugins' package references to CKli.Plugins.Core, to the standard plugins
        // and the CKli.Testing one in Tests/Plugins.Tests are all written as $(CKliVersion) and never rewritten.
        if( !EnsureCKliVersionProps( monitor, out bool preCompile ) )
        {
            return false;
        }
        // One-time migration of the Stacks created before $(CKliVersion) existed.
        // When in CKli itself, the CKli-Plugins solution uses project instead of package references:
        // there is no Directory.Packages.props, no version to migrate, so we skip this step.
        if( _definitionFile.World.StackName != "CKli"
            && (_versionChecked == null || !_versionChecked.Contains( Root )) )
        {
            if( !MigrateToCKliVersionProperty( monitor, out bool migrated ) )
            {
                return false;
            }
            preCompile |= migrated;
            _versionChecked ??= new HashSet<string>();
            _versionChecked.Add( Root );
        }
        // If we have a hook for the NuGet config file it must be applied now,
        // before any dotnet/nuget interaction.  
        if( _nuGetConfigFileHook != null && !ApplyNuGetConfigFileHook( monitor, NuGetConfigFile ) )
        {
            return false;
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

    // Called by World.AcquirePlugins when a plugin instantiation threw: the World has released its plugins and
    // this released the plugin factory (its AssemblyLoadContext is unloading).
    // The generated CKli.CompiledPlugins.cs is the usual suspect (a MissingMethodException typically): when it
    // exists it is deleted, then the plugins are recompiled and reloaded so that the reflection based factory has
    // a chance to work in this very run. Its deletion also bounds this recovery: once the file is gone, there is
    // nothing left to fix this way.
    // Any failure (plugins that cannot be unloaded, a failed compilation or load) falls back to the NoPluginFactory:
    // the World must open, "ckli plugin compile" and "ckli plugin info" are the way out.
    // False is returned only when we are already working without plugins.
    internal bool RecoverFromInstantiationError( IActivityMonitor monitor )
    {
        // ReleasePluginFactory() nulls the released factory but keeps the NoPluginFactory as-is.
        if( _pluginFactory != null && _pluginFactory == _onErrorPluginFactory ) return false;
        if( _pluginContext != null && File.Exists( CKliCompiledPluginsFile ) )
        {
            using( monitor.OpenInfo( "Recovering from the plugin instantiation error." ) )
            {
                if( FileHelper.DeleteFile( monitor, CKliCompiledPluginsFile )
                    && RecompileAndLoad( monitor, _pluginContext ) )
                {
                    return true;
                }
            }
        }
        monitor.Warn( "Unable to instantiate plugins. Working without plugins." );
        _pluginFactory = GetOnErrorPluginFactory();
        return true;
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
        var pluginContext = _pluginContext = new PluginCollectorContext( monitor, _definitionFile.World, pluginsConfiguration );
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
        if( _pluginFactory != null && _pluginFactory != _onErrorPluginFactory )
        {
            _pluginFactory.Dispose();
            _pluginFactory = null;
        }
    }

    // Writes "CKli.Version.props" when the version it carries is not the EffectiveCKliVersion and asks for a
    // recompilation in that case.
    // That recompilation is not optional: the generated CKli.CompiledPlugins.cs bakes a signature that includes
    // World.CKliVersion (PluginCollectorContext.ComputeSignature) and, in PluginCompileMode.None where no such
    // signature exists, a stale dll built against another CKli.Plugins.Core would only fail later, at plugin
    // instantiation (the MissingMethodException that RecoverFromInstantiationError has to catch).
    bool EnsureCKliVersionProps( IActivityMonitor monitor, out bool mustRecompile )
    {
        mustRecompile = false;
        try
        {
            var version = EffectiveCKliVersion;
            var content = string.Format( CKliVersionPropsPattern, version );
            if( File.Exists( CKliVersionProps ) && File.ReadAllText( CKliVersionProps ) == content )
            {
                return true;
            }
            monitor.Info( $"Setting CKliVersion to '{version}' in '{CKliVersionPropsFileName}'." );
            File.WriteAllText( CKliVersionProps, content );
            mustRecompile = true;
            // This file is generated: the Stack's .gitignore must cover it. Doing it here also repairs the
            // Stacks whose .gitignore predates it.
            return _definitionFile.World.Stack.EnsureGeneratedFilesIgnored( monitor )
                   && FileHelper.DeleteFile( monitor, CKliCompiledPluginsFile );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While writing '{CKliVersionProps}'.", ex );
            return false;
        }
    }

    // One-time migration of a plugin solution created before the $(CKliVersion) property existed: the literal
    // versions of CKli.Plugins.Core and of the standard plugins in "Directory.Packages.props" - and the
    // CKli.Testing one in "Tests/Plugins.Tests/Plugins.Tests.csproj" - become "$(CKliVersion)".
    //
    // After this, those 2 files stop moving. That is the point: they used to record which developer ran ckli
    // last, so 2 developers on 2 CKli versions left the Stack dirty and broke the next "ckli pull".
    //
    // The versions of the OTHER plugin packages ("ckli plugin add CKli.Xxx.Plugin@1.2.3") are deliberately left
    // as literals: those are genuine shared decisions that must stay tracked.
    //
    // For a LTS world, the literal version was also an implicit pin (this is what used to refuse a LTS world
    // opened with another CKli). The migration is lossless: it transfers that pin to the world definition file's
    // "CKliVersion" attribute, which LocalWorldName now checks.
    bool MigrateToCKliVersionProperty( IActivityMonitor monitor, out bool migrated )
    {
        migrated = false;
        try
        {
            var d = XDocument.Load( DirectoryPackageProps, LoadOptions.PreserveWhitespace );

            // Handles CKli.Plugins.Core that must exist.
            if( !ReadPackageVersion( monitor, d, "CKli.Plugins.Core", mustExist: true, DirectoryPackageProps, out XElement? ckliPluginsCore, out SVersion? literal ) )
            {
                return false;
            }
            Throw.DebugAssert( ckliPluginsCore != null );

            // Restores, on the world definition file, the pin that the literal version used to carry.
            // "literal" is null when this solution has already been migrated (the attribute is "$(CKliVersion)"):
            // there is then nothing to transfer.
            if( literal != null && !_definitionFile.World.IsDefaultWorld && _definitionFile.PinnedCKliVersion == null )
            {
                var ckliVersion = World.CKliVersion.Version;
                if( literal != ckliVersion )
                {
                    // Same refusal as before this migration existed: nothing is written, so re-running it with
                    // the appropriate CKli completes the migration.
                    monitor.Error( $"""
                                   This world '{_definitionFile.World.FullName}' is a Long Term Support world.
                                   It uses CKli in version '{literal}'. This CKli version is '{ckliVersion}'.
                                   Please use the appropriate CKli version.
                                   """ );
                    return false;
                }
                _definitionFile.EnsurePinnedCKliVersion( monitor, literal );
            }

            // The plugin solution's own "Directory.Build.props" must import the generated file BEFORE the
            // versions below reference $(CKliVersion): a solution that predates the property has no import at
            // all, and rewriting the versions without adding it would leave nothing able to restore.
            if( !EnsureVersionPropsImport( monitor, DirectoryBuildProps, RootVersionPropsImport, null, ref migrated ) )
            {
                return false;
            }

            // Tracked apart from "migrated": only a change to this document may rewrite the file (SafeSave
            // re-serializes it, so saving an untouched one would reformat it for nothing).
            bool packagesChanged = SetToCKliVersionProperty( monitor, ckliPluginsCore, DirectoryPackageProps );
            // Handles any standard plugin: their version is the CKli one.
            foreach( var name in PluginBase.StandardPluginNames )
            {
                if( !ReadPackageVersion( monitor, d, name, mustExist: false, DirectoryPackageProps, out XElement? standard, out _ ) )
                {
                    return false;
                }
                if( standard != null )
                {
                    packagesChanged |= SetToCKliVersionProperty( monitor, standard, DirectoryPackageProps );
                }
            }
            if( packagesChanged )
            {
                d.SafeSave( DirectoryPackageProps );
                migrated = true;
            }

            // Handles the CKli.Testing reference in 'Tests/Plugins.Tests/Plugins.Tests.csproj' if it exists.
            // The Plugins.Tests project doesn't use the Central Package Version: its PackageReference carries the
            // version itself. And since it has its own Directory.Build.props, that companion must import the
            // generated "CKli.Version.props" (see PluginTestsDirectoryBuildProps).
            if( File.Exists( PluginTestsCSProjFilePath ) )
            {
                if( !EnsureVersionPropsImport( monitor,
                                               PluginTestsDirectoryBuildProps,
                                               PluginTestsVersionPropsImport,
                                               DefaultPluginTestsDirectoryBuildProps,
                                               ref migrated ) )
                {
                    return false;
                }
                var testsCSProj = XDocument.Load( PluginTestsCSProjFilePath, LoadOptions.PreserveWhitespace );
                var ckliTesting = testsCSProj.Root?.Elements( XNames.ItemGroup )
                                             .Elements( XNames.PackageReference )
                                             .FirstOrDefault( e => e.Attribute( XNames.Include )?.Value == "CKli.Testing" );
                if( ckliTesting == null )
                {
                    // A Plugins.Tests that references CKli.Testing by project (this is what CKli's own stack does)
                    // has no version to migrate.
                    monitor.Trace( """
                        No <PackageReference Include="CKli.Testing" Version="..." /> element in 'Tests/Plugins.Tests/Plugins.Tests.csproj'.
                        Nothing to migrate there.
                        """ );
                }
                else if( SetToCKliVersionProperty( monitor, ckliTesting, PluginTestsCSProjFilePath ) )
                {
                    testsCSProj.SafeSave( PluginTestsCSProjFilePath );
                    migrated = true;
                }
            }

            if( migrated )
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
            monitor.Error( $"While migrating '{DirectoryPackageProps}' to the $(CKliVersion) property.", ex );
            return false;
        }

        static bool SetToCKliVersionProperty( IActivityMonitor monitor, XElement e, NormalizedPath file )
        {
            if( e.Attribute( XNames.Version )?.Value == CKliVersionPropertyRef ) return false;
            monitor.Info( $"""
                          Migrating '{file.LastPart}':
                          {e}
                          To use Version="{CKliVersionPropertyRef}".
                          """ );
            e.SetAttributeValue( XNames.Version, CKliVersionPropertyRef );
            return true;
        }

        // "version" is null when the element is missing (only allowed when mustExist is false) or when it
        // already carries the "$(CKliVersion)" property rather than a literal version.
        static bool ReadPackageVersion( IActivityMonitor monitor,
                                        XDocument d,
                                        string name,
                                        bool mustExist,
                                        NormalizedPath directoryPackageProps,
                                        out XElement? packageVersion,
                                        out SVersion? version )
        {
            version = null;
            packageVersion = d.Root?.Elements( XNames.ItemGroup )
                                    .Elements( XNames.PackageVersion )
                                    .FirstOrDefault( e => e.Attribute( XNames.Include )?.Value == name );
            if( packageVersion == null )
            {
                if( mustExist )
                {
                    monitor.Error( $"Unable to find <PackageVersion Include=\"{name}\" Version=\"...\" /> in '{directoryPackageProps}'." );
                    return false;
                }
                return true;
            }
            var v = packageVersion.Attribute( XNames.Version )?.Value;
            if( v != CKliVersionPropertyRef )
            {
                if( !SVersion.TryParse( v, out var parsed ) )
                {
                    monitor.Error( $"Invalid version in {packageVersion} (in '{directoryPackageProps}'): {parsed.ErrorMessage}." );
                    return false;
                }
                version = parsed;
            }
            return true;
        }
    }

    // Ensures that a "Directory.Build.props" imports the generated "CKli.Version.props".
    //
    // This is the other half of the migration and it is NOT optional: rewriting the package versions to
    // $(CKliVersion) in a solution whose "Directory.Build.props" predates the property would leave it
    // undefined, and nothing would restore at all.
    //
    // There are 2 such files. The plugin solution's own one, and the Plugins.Tests companion - that project
    // has its own (it works around https://github.com/dotnet/sdk/issues/45953) and MSBuild stops walking up at
    // the first "Directory.Build.props" it finds, so the property defined beside the plugin solution does NOT
    // reach it. Hence the 2 different relative paths in the imported lines.
    //
    // <paramref name="defaultContent"/> is null when the file must exist (the plugin solution's own one): a
    // missing one is a broken solution, not something to recreate from a template.
    bool EnsureVersionPropsImport( IActivityMonitor monitor,
                                   NormalizedPath path,
                                   string importLines,
                                   string? defaultContent,
                                   ref bool migrated )
    {
        try
        {
            if( !File.Exists( path ) )
            {
                if( defaultContent == null )
                {
                    monitor.Error( $"Missing '{path}'." );
                    return false;
                }
                monitor.Info( $"Creating '{path}'." );
                File.WriteAllText( path, defaultContent );
                migrated = true;
                return true;
            }
            var text = File.ReadAllText( path );
            if( text.Contains( CKliVersionPropsFileName, StringComparison.Ordinal ) )
            {
                return true;
            }
            // Text insert on purpose: re-serializing this file through XElement would reformat all of it.
            int start = text.IndexOf( "<Project", StringComparison.Ordinal );
            int end = start < 0 ? -1 : text.IndexOf( '>', start );
            if( end < 0 || text[end - 1] == '/' )
            {
                monitor.Error( $"""
                    Unable to find the opening <Project> element in '{path}'.
                    Please add these lines to it manually:
                    {importLines}
                    """ );
                return false;
            }
            monitor.Info( $"Importing '{CKliVersionPropsFileName}' from '{path}'." );
            File.WriteAllText( path, text.Insert( end + 1, Environment.NewLine + importLines ) );
            migrated = true;
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While updating '{path}'.", ex );
            return false;
        }
    }

    bool DoCompilePlugins( IActivityMonitor monitor )
    {
        var args = new StringBuilder( "build ", 256 );
        args.Append( CKliPluginsCSProj.LastPart );
        args.Append( " --tl:off --nologo" );
        args.Append( " -c " ).Append( _definitionFile.CompileMode == PluginCompileMode.Debug ? "Debug" : "Release" );
        // Global property: it wins over the generated "CKli.Version.props", so CKli's own build never depends on
        // that file being up to date. The IDE and "dotnet test" builds get the value from the file instead.
        args.Append( " -p:CKliVersion=" ).Append( EffectiveCKliVersion );
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
        return OnPluginChanged( monitor, world, reloadPlugins: false );
    }

    internal bool ForceRecompilePlugins( IActivityMonitor monitor, World world )
    {
        monitor.Info( "Forcing plugin recompilation." );
        return OnPluginChanged( monitor, world, reloadPlugins: true );
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
        return OnPluginChanged( monitor, world, false );
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
        return OnPluginChanged( monitor, world, true );
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
        return OnPluginChanged( monitor, world, reloadPlugins: true );
    }

    bool OnPluginChanged( IActivityMonitor monitor, World world, bool reloadPlugins )
    {
        world.ReleasePlugins();
        if( !FileHelper.DeleteFile( monitor, CKliCompiledPluginsFile ) )
        {
            return false;
        }
        // When plugins change, the 4 possible reasons are:
        // - SetPluginCompileMode: This doesn't change anything (at least should not).
        //                         There should be no reason to reload the plugin instances.
        //                         => reloadPlugins is false.
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
        if( !LoadPluginFactory( monitor, preCompile: true, out PluginCollectorContext? toRecompile )
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
            d.SafeSave( nuGetConfigFile );
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

                /// <summary>
                /// Main adapter between CKli.Plugins.Core and the plugins.
                /// </summary>
                public static class Plugins
                {
                    /// <summary>
                    /// Called by CKli.Plugins.Loader when no <c>static CKli.Plugins.CompiledPlugins.Get( PluginCollectorContext ctx )</c>
                    /// method exists or it returned null because <see cref="PluginCollectorContext.Signature"/> has changed.
                    /// </summary>
                    /// <param name="ctx">The collector context.</param>
                    /// <returns>The reflection based plugin factory.</returns>
                    public static IPluginFactory Register( PluginCollectorContext ctx )
                    {
                        return PluginCollector.Create( ctx ).BuildPluginFactory( [
                            // <AutoSection>
                            // </AutoSection>
                        ] );
                    }
                }
                                
                """;

    // The version is the $(CKliVersion) property that the generated CKliVersionPropsFileName carries: this file
    // is written once and never rewritten, so it doesn't record which developer ran ckli last.
    const string DefaultDirectoryPackageProps = """
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageVersion Include="CKli.Plugins.Core" Version="$(CKliVersion)" />
                  </ItemGroup>
                </Project>

                """;

    /// <summary>
    /// Gets the default "Directory.Build.props" file content: the {0} placeholder is for the <see cref="GetArtifactsPath(LocalWorldName)"/>.
    /// </summary>
    const string DefaultDirectoryBuildPropsPattern = """
                <Project>
                  <!-- Generated and git ignored: carries the $(CKliVersion) property for this developer's CKli. -->
                  <Import Project="$(MSBuildThisFileDirectory)CKli.Version.props" Condition="Exists('$(MSBuildThisFileDirectory)CKli.Version.props')" />
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ArtifactsPath>{0}</ArtifactsPath>
                    <ArtifactsPivots>run</ArtifactsPivots>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <Target Name="_CKliVersionRequired" BeforeTargets="Restore;CollectPackageReferences" Condition="'$(CKliVersion)' == ''">
                    <Error Text="CKliVersion is not set. Run any 'ckli' command in this Stack to generate CKli.Version.props." />
                  </Target>
                </Project>

                """;

    /// <summary>
    /// The name of the generated, git ignored file that carries the $(CKliVersion) property.
    /// <see cref="StackRepository.GeneratedFileIgnorePatterns"/> ignores it.
    /// </summary>
    public const string CKliVersionPropsFileName = "CKli.Version.props";

    /// <summary>
    /// The MSBuild reference to the $(CKliVersion) property, as it is written in the "Version" attribute of
    /// the CKli owned package references.
    /// </summary>
    internal const string CKliVersionPropertyRef = "$(CKliVersion)";

    /// <summary>
    /// Gets the "CKli.Version.props" content: the {0} placeholder is for the <see cref="EffectiveCKliVersion"/>.
    /// </summary>
    const string CKliVersionPropsPattern = """
                <Project>
                  <!--
                    Generated by CKli and git ignored: this is YOUR CKli version, not the world's.
                    A world that must impose one carries a CKliVersion attribute in its definition file
                    (this is what "ckli world lts create" writes on the LTS world it creates).
                  -->
                  <PropertyGroup>
                    <CKliVersion>{0}</CKliVersion>
                  </PropertyGroup>
                </Project>

                """;

    /// <summary>
    /// Gets the content of the "Tests/Plugins.Tests/Directory.Build.props" companion when it must be created.
    /// MSBuild stops walking up at the first "Directory.Build.props" it finds, so this project does NOT see
    /// the one at the root of the plugin solution: it has to import the generated file itself.
    /// </summary>
    const string DefaultPluginTestsDirectoryBuildProps = """
                <Project>
                  <!--
                  This props works around https://github.com/dotnet/sdk/issues/45953.
                  -->
                  <Import Project="$(MSBuildThisFileDirectory)../../CKli.Version.props" Condition="Exists('$(MSBuildThisFileDirectory)../../CKli.Version.props')" />
                </Project>

                """;

    // The lines inserted into an already existing "Tests/Plugins.Tests/Directory.Build.props".
    const string PluginTestsVersionPropsImport = """
                  <Import Project="$(MSBuildThisFileDirectory)../../CKli.Version.props" Condition="Exists('$(MSBuildThisFileDirectory)../../CKli.Version.props')" />
                """;

    // The lines inserted into the plugin solution's "Directory.Build.props" when it predates $(CKliVersion).
    // Without this, migrating "Directory.Packages.props" to $(CKliVersion) would leave the property undefined
    // and nothing would restore at all.
    const string RootVersionPropsImport = """
                  <!-- Generated and git ignored: carries the $(CKliVersion) property for this developer's CKli. -->
                  <Import Project="$(MSBuildThisFileDirectory)CKli.Version.props" Condition="Exists('$(MSBuildThisFileDirectory)CKli.Version.props')" />
                  <Target Name="_CKliVersionRequired" BeforeTargets="Restore;CollectPackageReferences" Condition="'$(CKliVersion)' == ''">
                    <Error Text="CKliVersion is not set. Run any 'ckli' command in this Stack to generate CKli.Version.props." />
                  </Target>
                """;

#pragma warning restore IDE1006 // Naming Styles

}

