using CK.Core;
using CSemVer;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[SetUpFixture]
static partial class TestEnv
{
    readonly static NormalizedPath _remotesPath = TestHelper.TestProjectFolder.AppendPart( "Remotes" );
    readonly static NormalizedPath _nugetSourcePath = TestHelper.TestProjectFolder.AppendPart( "NuGetSource" );
    readonly static NormalizedPath _packagedPluginsPath = TestHelper.TestProjectFolder.AppendPart( "PackagedPlugins" );
    static SVersion? _cKliPluginsCoreVersion;
    static XDocument? _packagedDirectoryPackagesProps;
    static Dictionary<string, RemotesCollection>? _readOnlyRemotes;
    static RemotesCollection? _inUse;

    [OneTimeSetUp]
    public static void SetupEnv() => TestHelper.OnlyOnce( Initialize );

    [OneTimeTearDown]
    public static void TearDownEnv()
    {
        _packagedDirectoryPackagesProps?.SaveWithoutXmlDeclaration( _packagedPluginsPath.AppendPart( "Directory.Packages.props" ) );
    }

    static void Initialize()
    {
        CKliRootEnv.Initialize( "Test", screen: new StringScreen() );
        World.PluginLoader = CKli.Loader.PluginLoadContext.Load;
        InitializeRemotes();
        InitializeNuGetSource();
    }

    static void InitializeNuGetSource()
    {
        if( !Directory.Exists( _nugetSourcePath ) )
        {
            Directory.CreateDirectory( _nugetSourcePath );
        }
        PluginMachinery.NuGetConfigFileHook = ( monitor, nuGetXmlDoc ) =>
        {
            NuGetHelper.SetOrRemoveNuGetSource( monitor,
                                                nuGetXmlDoc,
                                                "test-override",
                                                _nugetSourcePath,
                                                "CKli.Core", "CKli.Plugins.Core", "CKli.*.Plugin" )
                       .ShouldBeTrue();
        };
        var corePath = CopyMostRecentPackageToNuGetSource( "CKli.Core" );
        var pluginsPath = CopyMostRecentPackageToNuGetSource( "CKli.Plugins.Core" );
        
        foreach( var nuget in Directory.EnumerateFiles( _nugetSourcePath ) )
        {
            var p = new NormalizedPath( nuget );
            if( p != corePath && p != pluginsPath )
            {
                FileHelper.DeleteFile( TestHelper.Monitor, p ).ShouldBeTrue();
            }
        }
        _cKliPluginsCoreVersion = SVersion.Parse( pluginsPath.LastPart["CKli.Plugins.Core.".Length..^".nupkg".Length] );

        static NormalizedPath CopyMostRecentPackageToNuGetSource( string projectFolder )
        {
            var projectBin = TestHelper.SolutionFolder.Combine( $"{projectFolder}/bin/{TestHelper.BuildConfiguration}" );
            var (path, date) = Directory.EnumerateFiles( projectBin, "*.nupkg" )
                                 .Select( file => (file, File.GetLastWriteTimeUtc( file )) )
                                 .OrderByDescending( e => e.Item2 )
                                 .FirstOrDefault();
            if( path == null )
            {
                throw new Exception( $"Unable to find any *.nupkg file in '{projectBin}'." );
            }
            var target = _nugetSourcePath.AppendPart( Path.GetFileName( path ) );
            if( date != File.GetLastWriteTimeUtc( target ) )
            {
                File.Copy( path, target, overwrite: true );
                File.SetLastWriteTimeUtc( target, date );
            }
            return target;
        }
    }

    static void InitializeRemotes()
    {
        var zipPath = _remotesPath.AppendPart( "Remotes.zip" );
        var zipTime = File.GetLastWriteTimeUtc( zipPath );
        if( !Directory.EnumerateDirectories( _remotesPath ).Any()
            || LastWriteTimeChanged( zipTime ) )
        {
            using( TestHelper.Monitor.OpenInfo( $"Last write time of 'Remotes/' differ from 'Remotes/Remotes.zip'. Restoring remotes from zip." ) )
            {
                foreach( var stack in Directory.EnumerateDirectories( _remotesPath ) )
                {
                    foreach( var repository in Directory.EnumerateDirectories( stack ) )
                    {
                        if( !ClonedPaths.DeleteClonedFolderOnly( repository ) )
                        {
                            TestHelper.Monitor.Warn( $"Folder '{repository}' didn't contain a .git folder. All folders in Remotes/<stack> should be git working folders." );
                        }
                    }
                }
                // Allow overwriting .gitignore file.
                ZipFile.ExtractToDirectory( zipPath, _remotesPath, overwriteFiles: true );
                SetLastWriteTime( zipTime );
            }
        }
        _readOnlyRemotes = Directory.EnumerateDirectories( _remotesPath )
                                    .Select( d => new RemotesCollection( d, true ) )
                                    .ToDictionary( r => r.Name );

        static bool LastWriteTimeChanged( DateTime zipTime )
        {
            if( Directory.GetLastWriteTimeUtc( _remotesPath ) != zipTime )
            {
                return true;
            }
            foreach( var sub in Directory.EnumerateDirectories( _remotesPath ) )
            {
                if( Directory.GetLastWriteTimeUtc( sub ) != zipTime )
                {
                    return true;
                }
            }
            return false;
        }

        static void SetLastWriteTime( DateTime zipTime )
        {
            Directory.SetLastWriteTimeUtc( _remotesPath, zipTime );
            foreach( var sub in Directory.EnumerateDirectories( _remotesPath ) )
            {
                Directory.SetLastWriteTimeUtc( sub, zipTime );
            }
        }
    }

    /// <summary>
    /// Activates the <see cref="IRemotesCollection"/> that must exist.
    /// </summary>
    /// <param name="name">The <see cref="IRemotesCollection.Name"/> to use.</param>
    /// <returns>The active remotes collection.</returns>
    public static IRemotesCollection UseReadOnly( string name )
    {
        Throw.DebugAssert( _readOnlyRemotes != null );
        if( _inUse != null )
        {
            if( _inUse.Name == name ) return _inUse;
            if( !_inUse.IsReadOnly )
            {
                // Close Current. TODO.
            }
        }
        var newOne = _readOnlyRemotes[name];
        //
        // Now useless (but still possible):
        // "Repositories Proxy" has been integrated in WorldDefinitionFile.
        //
        //  // Allows Url to already be Url (to support tests that alter the definition files).
        //  WorldDefinitionFile.RepositoryUrlHook = ( monitor, sUri ) => Uri.TryCreate( sUri, UriKind.Absolute, out var uri )
        //                                                               ? sUri
        //                                                               : newOne.GetUriFor( sUri ).ToString();

        return _inUse = newOne;
    }

    /// <summary>
    /// Gets the the version "CKli.Core" and "CKi.Plugins.Core" that have been compiled and ar available
    /// in the "NuGetSource/" folder.
    /// </summary>
    public static SVersion CKliPluginsCoreVersion => _cKliPluginsCoreVersion!;

    /// <summary>
    /// Compiles and packs the specified "PcackgedPlugins/<paramref name="projectName"/>" and
    /// make it available in the "NuGetSource" folder.
    /// </summary>
    /// <param name="projectName">The project name (in Plugins/ folder).</param>
    /// <param name="version">Optional version that can differ from the <see cref="CKliPluginsCoreVersion"/>.</param>
    public static void EnsurePluginPackage( string projectName, string? version = null )
    {
        if( _packagedDirectoryPackagesProps == null )
        {
            // Setting the "CKli.Plugins.Core" package in the current version (from the NuGetSource).
            // The XDocument is cloned, the original one will be restored by TearDownEnv.
            _cKliPluginsCoreVersion.ShouldNotBeNull();
            var pathDirectoryPackages = _packagedPluginsPath.AppendPart( "Directory.Packages.props" );
            _packagedDirectoryPackagesProps = XDocument.Load( pathDirectoryPackages, LoadOptions.PreserveWhitespace );

            var clone = new XDocument( _packagedDirectoryPackagesProps );
            clone.Root.ShouldNotBeNull();
            clone.Root.Elements( "ItemGroup" ).Elements( "PackageVersion" )
                 .First( e => e.Attribute( "Include" )?.Value == "CKli.Plugins.Core" )
                 .SetAttributeValue( "Version", _cKliPluginsCoreVersion );

            clone.SaveWithoutXmlDeclaration( pathDirectoryPackages );
        }

        // Clear any cached version of the new package.
        NuGetHelper.ClearGlobalCache( TestHelper.Monitor, projectName, null );

        var v = version != null ? SVersion.Parse( version ) : _cKliPluginsCoreVersion;

        var path = _packagedPluginsPath.AppendPart( projectName );
        var args = $"""pack -tl:off --no-dependencies -o "{_nugetSourcePath}" -c {TestHelper.BuildConfiguration} /p:IsPackable=true;Version={v}""";
        using var _ = TestHelper.Monitor.OpenInfo( $"""
            Ensure plugin package '{projectName}':
            dotnet {args}
            """ );
        ProcessRunner.RunProcess( TestHelper.Monitor.ParallelLogger, "dotnet", args, path, null )
            .ShouldBe( 0 );
    }

}
