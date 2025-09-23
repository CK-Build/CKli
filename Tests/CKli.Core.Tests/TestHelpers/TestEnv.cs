using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli.Core.Tests;

[SetUpFixture]
static partial class TestEnv
{
    readonly static NormalizedPath _remotesPath = TestHelper.TestProjectFolder.AppendPart( "Remotes" );
    readonly static NormalizedPath _nugetSourcePath = TestHelper.TestProjectFolder.AppendPart( "NuGetSource" );
    static Dictionary<string, RemotesCollection>? _readOnlyRemotes;
    static RemotesCollection? _inUse;

    [OneTimeSetUp]
    public static void EnsureRemotes() => TestHelper.OnlyOnce( Initialize );

    static void Initialize()
    {
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
            PluginMachinery.SetOrRemoveNuGetSource( monitor,
                                                    nuGetXmlDoc,
                                                    "test-override",
                                                    _nugetSourcePath,
                                                    "CKli.Core", "CKli.Plugins.Core" )
                           .ShouldBeTrue();
        };
        CopyMostRecentPackageToNuGetSource( "CKli.Core" );
        CopyMostRecentPackageToNuGetSource( "CKli.Plugins.Core" );

        static void CopyMostRecentPackageToNuGetSource( string projectFolder )
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
        }
    }

    static void InitializeRemotes()
    {
        var zipPath = _remotesPath.AppendPart( "Remotes.zip" );
        var zipTime = File.GetLastWriteTimeUtc( zipPath );
        if( LastWriteTimeChanged( zipTime ) )
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

}
