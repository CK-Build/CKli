using CK.Core;
using CSemVer;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[SetUpFixture]
static partial class TestEnv
{
    readonly static NormalizedPath _remotesPath = TestHelper.TestProjectFolder.AppendPart( "Remotes" );
    readonly static NormalizedPath _barePath = _remotesPath.AppendPart( "bare" );
    readonly static NormalizedPath _nugetSourcePath = TestHelper.TestProjectFolder.AppendPart( "NuGetSource" );
    readonly static NormalizedPath _packagedPluginsPath = TestHelper.TestProjectFolder.AppendPart( "PackagedPlugins" );
    static NormalizedPath _clonedPath = TestHelper.TestProjectFolder.AppendPart( "Cloned" );

    static SVersion? _cKliPluginsCoreVersion;
    static XDocument? _packagedDirectoryPackagesProps;
    static Dictionary<string, RemotesCollection>? _readOnlyRemotes;

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
        var remoteIndexPath = _barePath.AppendPart( "Remotes.txt" );

        var zipPath = _remotesPath.AppendPart( "Remotes.zip" );
        var zipTime = File.GetLastWriteTimeUtc( zipPath );
        if( !Directory.EnumerateDirectories( _remotesPath ).Any()
            || LastWriteTimeChanged( zipTime ) )
        {
            using( TestHelper.Monitor.OpenInfo( $"Last write time of 'Remotes/' differ from 'Remotes/Remotes.zip'. Restoring remotes from zip." ) )
            {
                RestoreRemotesZipAndCreateBareRepositories( remoteIndexPath, zipPath, zipTime );
            }
        }
        _readOnlyRemotes = File.ReadAllLines( remoteIndexPath )
                               .Select( l => l.Split( '/' ) )
                               .GroupBy( names => names[0], names => names[1] )
                               .Select( g => new RemotesCollection( g.Key, g.ToArray() ) ).
                               ToDictionary( r => r.Name ); 

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

        static void RestoreRemotesZipAndCreateBareRepositories( NormalizedPath remoteIndexPath, NormalizedPath zipPath, DateTime zipTime )
        {
            // Cleanup "bare/" content if it exists and delete any existing unzipped repositories.
            foreach( var stack in Directory.EnumerateDirectories( _remotesPath ) )
            {
                var stackName = Path.GetFileName( stack.AsSpan() );
                if( stackName.Equals( "bare", StringComparison.OrdinalIgnoreCase ) )
                {
                    foreach( var openedBare in Directory.EnumerateDirectories( stack ) )
                    {
                        DeleteFolder( openedBare );
                    }
                    foreach( var zippedBareOrRemotesIndex in Directory.EnumerateFiles( stack ) )
                    {
                        Throw.Assert( Path.GetFileName( zippedBareOrRemotesIndex ) == "Remotes.txt"
                                      || zippedBareOrRemotesIndex.EndsWith( ".zip" ) );
                        DeleteFile( zippedBareOrRemotesIndex );
                    }
                }
                else
                {
                    foreach( var repository in Directory.EnumerateDirectories( stack ) )
                    {
                        if( !FileHelper.DeleteClonedFolderOnly( TestHelper.Monitor, repository, out var _ ) )
                        {
                            TestHelper.Monitor.Warn( $"Folder '{repository}' didn't contain a .git folder. All folders in Remotes/<stack> should be git working folders." );
                        }
                    }
                }
            }

            // Extracts Remotes.zip content.
            // Disallow overwriting: .gitignore file and README.md must not be in the Zip archive.
            ZipFile.ExtractToDirectory( zipPath, _remotesPath, overwriteFiles: false );
            SetLastWriteTime( zipTime );
            // Fills the bare/ with the .zip of the bare repositories and creates the Remotes.txt
            // index file.
            var remotesContent = new StringBuilder();
            Directory.CreateDirectory( _barePath );
            foreach( var stack in Directory.EnumerateDirectories( _remotesPath ) )
            {
                var stackName = Path.GetFileName( stack.AsSpan() );
                if( !stackName.Equals( "bare", StringComparison.OrdinalIgnoreCase ) )
                {
                    var bareStack = Path.Combine( _barePath, new string( stackName ) );
                    foreach( var repository in Directory.EnumerateDirectories( stack ) )
                    {
                        var src = new DirectoryInfo( Path.Combine( repository, ".git" ) );
                        var dst = Path.Combine( bareStack, Path.GetFileName( repository ), ".git" );
                        var target = new DirectoryInfo( dst );
                        FileUtil.CopyDirectory( src, target );
                        var exitCode = ProcessRunner.RunProcess( TestHelper.Monitor.ParallelLogger,
                                                                 "git",
                                                                 "config core.bare true",
                                                                 dst );
                        if( exitCode != 0 )
                        {
                            Throw.CKException( $"Failed to configure bare repository '{dst}'." );
                        }
                        remotesContent.AppendLine( $"{stackName}/{Path.GetFileName( repository )}" );
                    }
                    ZipFile.CreateFromDirectory( bareStack, bareStack + ".zip" );
                }
            }
            File.WriteAllText( remoteIndexPath, remotesContent.ToString() );
        }
    }

    /// <summary>
    /// Obtains a clean (unmodified) <see cref="RemotesCollection"/> that must exist.
    /// </summary>
    /// <param name="name">The <see cref="IRemotesCollection.Name"/> to use.</param>
    /// <returns>The active remotes collection.</returns>
    public static RemotesCollection OpenRemotes( string name )
    {
        Throw.DebugAssert( _readOnlyRemotes != null );
        var r = _readOnlyRemotes[name];
        // Deletes the current repository that may have been modified
        // and extracts a brand new bare git repository.
        var path = _barePath.AppendPart( r.Name );
        DeleteFolder( path );
        ZipFile.ExtractToDirectory( path + ".zip", path, overwriteFiles: false );
        return r;
    }

    /// <summary>
    /// Gets the the version "CKli.Core" and "CKi.Plugins.Core" that have been compiled and are available
    /// in the "NuGetSource/" folder.
    /// </summary>
    public static SVersion CKliPluginsCoreVersion => _cKliPluginsCoreVersion!;

    /// <summary>
    /// Gets the path to the "Cloned" folder where stacks are cloned from the "Remotes" for each
    /// test that calls <see cref="EnsureCleanFolder(string?, bool)"/>.
    /// </summary>
    public static NormalizedPath ClonedPath => _clonedPath;

    /// <summary>
    /// Called by tests to cleanup their respective "Cloned/&lt;test-name&gt;" where they can clone
    /// the stack they want from the "Remotes".
    /// </summary>
    /// <param name="name">The test name.</param>
    /// <param name="clearStackRegistryFile">True to clear the stack registry (<see cref="StackRepository.ClearRegistry"/>).</param>
    /// <returns>A <see cref="CKliEnv"/> where <see cref="CKliEnv.CurrentDirectory"/> is the dedicated test repository.</returns>
    public static CKliEnv EnsureCleanFolder( [CallerMemberName] string? name = null, bool clearStackRegistryFile = true )
    {
        var path = _clonedPath.AppendPart( name );
        if( Directory.Exists( path ) )
        {
            RemoveAllReadOnlyAttribute( path );
            TestHelper.CleanupFolder( path, ensureFolderAvailable: true );
        }
        else
        {
            Directory.CreateDirectory( path );
        }
        if( clearStackRegistryFile )
        {
            Throw.CheckState( StackRepository.ClearRegistry( TestHelper.Monitor ) );
        }
        return new CKliEnv( path, screen: new StringScreen() );

        static void RemoveAllReadOnlyAttribute( string folder )
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.System
            };
            foreach( var f in Directory.EnumerateFiles( folder, "*", options ) )
            {
                File.SetAttributes( f, FileAttributes.Normal );
            }
        }
    }

    /// <summary>
    /// Compiles and packs the specified "PackagedPlugins/<paramref name="projectName"/>" and
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

    /// <summary>
    /// Ensures that <see cref="FileHelper.TryMoveFolder(IActivityMonitor, NormalizedPath, NormalizedPath, HashSet{NormalizedPath}?)"/>
    /// succeeds.
    /// </summary>
    /// <param name="from">The folder path to move.</param>
    /// <param name="to">The renamed or moved destination folder path.</param>
    public static void MoveFolder( NormalizedPath from, NormalizedPath to )
    {
        FileHelper.TryMoveFolder( TestHelper.Monitor, from, to ).ShouldBeTrue();
    }

    /// <summary>
    /// Ensures that <see cref="FileHelper.DeleteFolder(IActivityMonitor, string)"/> succeeds.
    /// </summary>
    /// <param name="path">The folder path to delete.</param>
    public static void DeleteFolder( string path )
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, path ).ShouldBeTrue();
    }

    /// <summary>
    /// Ensures that <see cref="FileHelper.DeleteFile(IActivityMonitor, string)"/> succeeds.
    /// </summary>
    /// <param name="path">The file path to delete.</param>
    public static void DeleteFile( string path )
    {
        FileHelper.DeleteFile( TestHelper.Monitor, path ).ShouldBeTrue();
    }

}
