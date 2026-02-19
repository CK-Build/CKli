
using CK.Core;
using CK.Testing;
using CKli.Core;
using LibGit2Sharp;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli;

/// <summary>
/// Provides extension members to <see cref="IMonitorTestHelper"/> that manage the "Remotes/"
/// and the "Cloned/" folders. 
/// <para>
/// This class has a static initializer that setups the CKli context so it can be used
/// in NUnit context.
/// </para>
/// </summary>
public static partial class CKliTestHelperExtensions
{
    readonly static NormalizedPath _remotesPath;
    readonly static NormalizedPath _barePath;
    readonly static NormalizedPath _clonedPath;
    readonly static WorldName _defaultWorldName;
    readonly static Dictionary<string, RemotesCollection> _remoteRepositories;
    readonly static XElement _hostPluginsConfiguration;

    static CKliTestHelperExtensions()
    {
        _remotesPath = TestHelper.TestProjectFolder.AppendPart( "Remotes" );
        _barePath = _remotesPath.AppendPart( "bare" );
        _clonedPath = TestHelper.TestProjectFolder.AppendPart( "Cloned" );

        var pluginFolderName = TestHelper.TestProjectFolder.Parts[^3];
        Throw.CheckState( pluginFolderName.Contains( "-Plugins" ) );
        int idx = pluginFolderName.IndexOf( "-Plugins" );
        var stackName = pluginFolderName.Substring( 0, idx );
        var ltsName = pluginFolderName.Substring( idx + 8 );
        _defaultWorldName = new WorldName( stackName, ltsName );

        // We must ensure that the CKli.Plugins.CompiledPlugins is available before loading this assembly
        // in the load context because once loaded, we won't be able to "update" it if the compiled plugins
        // weren't available...
        // We may use a ReflectionOnly context or use the Meta API but this costs. We simply consider that
        // if the CKli.CompiledPlugins.cs file is present, then it's fine: we are in a test project that
        // depends on the CKli.Plugins project, so when it is compiled, the CKli.Plugins is also compiled.
        //
        // We check that CompileMode is not None and that no plugins are disabled before.
        // If the user deleted the CKli.CompiledPlugins.cs, he must run "ckli plugin info" to restore the
        // compiled plugins.
        //
        _hostPluginsConfiguration = ReadStackPluginConfiguration( TestHelper.Monitor, _defaultWorldName );

        var ckliPluginsCompiledFile = TestHelper.SolutionFolder.AppendPart( pluginFolderName ).AppendPart( "CKli.Plugins" ).AppendPart( "CKli.CompiledPlugins.cs" );
        if( !File.Exists( ckliPluginsCompiledFile ) )
        {
            Throw.InvalidOperationException( $"The compiled plugins source code generated file is missing: '{ckliPluginsCompiledFile}'." );
        }
        var runFolder = TestHelper.SolutionFolder.Combine( PluginMachinery.GetLocalRunFolder( pluginFolderName ) );
        var ckliPluginFilePath = runFolder.AppendPart( "CKli.Plugins.dll" );
        if( !File.Exists( ckliPluginFilePath ) )
        {
            Throw.InvalidOperationException( $"The compiled plugins file is missing: '{ckliPluginFilePath}'." );
        }
        var f = GetPluginFactory( ckliPluginFilePath );
        if( f == null )
        {
            Throw.InvalidOperationException( "Unable to get the plugin factory from the compiled plugins." );
        }
        World.DirectPluginFactory = f;
        CKliRootEnv.Initialize( _defaultWorldName.FullName, screen: new StringScreen(), findCurrentStackPath: false );

        _remoteRepositories = InitializeRemotes();

        static XElement ReadStackPluginConfiguration( IActivityMonitor monitor, WorldName worldHostName )
        {
            // Reads the host's default world definition definition file <Plugins> element.
            XElement? stackPlugins = null;
            try
            {
                var stackDefinitionFile = TestHelper.SolutionFolder.AppendPart( $"{worldHostName}.xml" );
                XDocument stackDefinitionDoc = XDocument.Load( stackDefinitionFile, LoadOptions.PreserveWhitespace );
                stackPlugins = stackDefinitionDoc.Root?.Element( "Plugins" );
                if( stackPlugins == null )
                {
                    monitor.Error( "The Stack's World '{worldHostName}' has no <Plugins> element." );
                }
                else if( stackPlugins.Elements().Any( e => (bool?)e.Attribute( "Disabled" ) is true ) )
                {
                    monitor.Error( $"""
                                    The Stack's World '{worldHostName}' <Plugins> element must no have ANY disabled plugins:
                                    {stackPlugins}
                                    """ );
                    stackPlugins = null;
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While reading the Stack's World '{worldHostName}' <Plugins> element.", ex );
                stackPlugins = null;
            }
            if( stackPlugins == null )
            {
                Throw.InvalidOperationException( $"Unable to read the Stack's World '{worldHostName}' <Plugins> element." );
            }
            return stackPlugins;
        }

        static Func<IPluginFactory>? GetPluginFactory( NormalizedPath ckliPluginFilePath )
        {
            var ckliPlugins = Assembly.LoadFrom( ckliPluginFilePath );
            var compiled = ckliPlugins.GetType( "CKli.Plugins.CompiledPlugins" );
            var m = compiled?.GetMethod( "UncheckedGet" );
            return m == null
                    ? null
                    : () => (IPluginFactory)m.Invoke( null, [] )!;
        }

        static Dictionary<string, RemotesCollection> InitializeRemotes()
        {
            var remoteIndexPath = _barePath.AppendPart( "Remotes.txt" );

            var zipPath = _remotesPath.AppendPart( "Remotes.zip" );
            var zipTime = File.GetLastWriteTimeUtc( zipPath );
            if( !File.Exists( remoteIndexPath )
                || File.GetLastWriteTimeUtc( remoteIndexPath ) != zipTime )
            {
                using( TestHelper.Monitor.OpenInfo( $"Last write time of 'Remotes/' differ from 'Remotes/Remotes.zip'. Restoring remotes from zip." ) )
                {
                    RestoreRemotesZipAndCreateBareRepositories( remoteIndexPath, zipPath, zipTime );
                }
            }
            return File.ReadAllLines( remoteIndexPath )
                        .Select( l => l.Split( '/' ) )
                        .GroupBy( names => names[0], names => names[1] )
                        .Select( g => new RemotesCollection( g.Key, g.ToArray() ) )
                        .ToDictionary( r => r.FullName );

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
                            FileHelper.DeleteFolder( TestHelper.Monitor, openedBare ).ShouldBeTrue();
                        }
                        foreach( var zippedBareOrRemotesIndex in Directory.EnumerateFiles( stack ) )
                        {
                            Throw.Assert( Path.GetFileName( zippedBareOrRemotesIndex ) == "Remotes.txt"
                                          || zippedBareOrRemotesIndex.EndsWith( ".zip" ) );
                            FileHelper.DeleteFile( TestHelper.Monitor, zippedBareOrRemotesIndex ).ShouldBeTrue();
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
                // Fills the bare/ with the .zip of the bare repositories and creates the Remotes.txt
                // index file.
                var remotesIndex = new StringBuilder();
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
                            using var r = new Repository( dst );
                            r.Config.Set( "core.bare", true );
                            remotesIndex.AppendLine( $"{stackName}/{Path.GetFileName( repository )}" );
                        }
                        ZipFile.CreateFromDirectory( bareStack, bareStack + ".zip" );
                    }
                }
                File.WriteAllText( remoteIndexPath, remotesIndex.ToString() );
                File.SetLastWriteTimeUtc( remoteIndexPath, zipTime );
            }
        }

    }

    extension( IMonitorTestHelper helper )
    {
        /// <summary>
        /// Gets the "Remotes/" path in the test project folder where
        /// remote repositories are defined and managed.
        /// </summary>
        public NormalizedPath CKliRemotesPath => _remotesPath;

        /// <summary>
        /// Gets the "Cloned/" path in the test project folder where
        /// remote repositories are cloned and used by tests (see <see cref="RemotesCollection.Clone"/>).
        /// </summary>
        public NormalizedPath CKliClonedPath => _clonedPath;

        /// <summary>
        /// Gets the default World name that contains the plugins (and these tests).
        /// </summary>
        public WorldName CKliDefaultWorldName => _defaultWorldName;

        /// <summary>
        /// Gets the <see cref="StackRepository.StackWorkingFolder"/> (the ".PrivateStack" or ".PublicStack" folder)
        /// of the host stack.
        /// </summary>
        public NormalizedPath CKliStackWorkingFolder => helper.SolutionFolder;
    }

    /// <summary>
    /// Must be called by tests to cleanup their respective "Cloned/&lt;test-name&gt;" where they can clone
    /// the stacks they want from the "Remotes" thanks to <see cref="RemotesCollection.Clone(NormalizedPath, Action{IActivityMonitor, XElement}?)"/>.
    /// </summary>
    /// <param name="methodTestName">The test name.</param>
    /// <param name="clearStackRegistryFile">True to clear the stack registry (<see cref="StackRepository.ClearRegistry"/>).</param>
    /// <returns>The path to the cleaned folder.</returns>
    public static NormalizedPath InitializeClonedFolder( this IMonitorTestHelper helper, [CallerMemberName] string? methodTestName = null, bool clearStackRegistryFile = true )
    {
        var path = _clonedPath.AppendPart( methodTestName );
        if( Directory.Exists( path ) )
        {
            RemoveAllReadOnlyAttribute( path );
            helper.CleanupFolder( path, ensureFolderAvailable: true );
        }
        else
        {
            Directory.CreateDirectory( path );
        }
        if( clearStackRegistryFile )
        {
            Throw.CheckState( StackRepository.ClearRegistry( TestHelper.Monitor ) );
        }
        return path;

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
    /// Obtains a clean (unmodified) <see cref="RemotesCollection"/> that must exist.
    /// </summary>
    /// <param name="fullName">The <see cref="RemotesCollection.FullName"/> to use.</param>
    /// <returns>The remotes collection.</returns>
    public static RemotesCollection OpenRemotes( this IMonitorTestHelper helper, string fullName )
    {
        Throw.DebugAssert( _remoteRepositories != null );
        var r = _remoteRepositories[fullName];
        // Deletes the current repository that may have been modified
        // and extracts a brand new bare git repository.
        var path = _barePath.AppendPart( r.FullName );
        FileHelper.DeleteFolder( TestHelper.Monitor, path ).ShouldBeTrue();
        ZipFile.ExtractToDirectory( path + ".zip", path, overwriteFiles: false );
        return r;
    }

    /// <summary>
    /// Create or modify a "CKliTestModification.cs" file in the <paramref name="projectFolder"/> and
    /// creates a new commit on a specified branch or on the currently checked out branch.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="projectFolder">The project  folder (eg. "CKt.Core") relative to <see cref="CKliEnv.CurrentDirectory"/> (can be absolute).</param>
    /// <param name="branchName">The branch name to update (or null to touch the working folder and commit on the current repository head).</param>
    /// <param name="commitMessage">Optional commit message.</param>
    public static void ModifyAndCreateCommit( this IMonitorTestHelper helper, CKliEnv context, NormalizedPath projectFolder, string? branchName, string? commitMessage = null )
    {
        var projectPath = context.CurrentDirectory.Combine( projectFolder ).ResolveDots();

        if( string.IsNullOrEmpty( commitMessage ) )
        {
            commitMessage = $"// Touching {projectFolder.LastPart}.";
        }

        NormalizedPath gitPath = Repository.Discover( projectPath );
        if( gitPath.IsEmptyPath )
        {
            if( !Directory.Exists( projectPath ) )
            {
                Throw.ArgumentException( nameof( projectFolder ), $"""
                    Path '{projectPath}' doesn't exist. It has been combined from:
                    context.CurrentDirectory = '{context.CurrentDirectory}'
                    and:
                    projectFolder: '{projectFolder}'
                    """ );
            }
            Throw.ArgumentException( nameof( projectFolder ), $"Unable to find the .git folder from '{projectPath}'." );
        }
        gitPath = gitPath.RemoveLastPart();
        using( var git = new Repository( gitPath ) )
        {
            if( branchName != null )
            {
                var b = git.Branches[branchName];
                if( b == null )
                {
                    Throw.ArgumentException( $"Unable to find branch '{branchName}'." );
                }
                if( !b.IsCurrentRepositoryHead )
                {
                    TreeDefinition tDef = TreeDefinition.From( b.Tip.Tree );
                    gitPath.TryGetRelativePathTo( projectPath, out var relativeProjectPath ).ShouldBeTrue();
                    if( tDef[relativeProjectPath] == null )
                    {
                        Throw.ArgumentException( $"Unable to find '{relativeProjectPath}' in branch '{branchName}'." );
                    }
                    var filePath = relativeProjectPath.AppendPart( "CKliTestModification.cs" );

                    string text = "// Created";
                    TreeEntryDefinition? fileDef = tDef[filePath];
                    if( fileDef != null )
                    {
                        if( fileDef.TargetType != TreeEntryTargetType.Blob || fileDef.Mode != Mode.NonExecutableFile )
                        {
                            Throw.InvalidOperationException( $"Entry '{filePath}' in branch '{branchName}' is not a non executable Blob." );
                        }
                        var blob = git.Lookup<Blob>( fileDef.TargetId );
                        text = blob.GetContentText() + $"{Environment.NewLine}{DateTime.UtcNow}";
                    }
                    ObjectId textId = git.ObjectDatabase.Write<Blob>( Encoding.UTF8.GetBytes( text ) );
                    tDef.Add( filePath, textId, Mode.NonExecutableFile );
                    var newTree = git.ObjectDatabase.CreateTree( tDef );
                    var newCommit = git.ObjectDatabase.CreateCommit( context.Committer, context.Committer, commitMessage, newTree, [b.Tip], prettifyMessage: true );
                    git.Refs.UpdateTarget( b.Reference, newCommit.Id, null );
                    return;
                }
            }
            // Either the branchName is null (the user wants to work in the head) or the
            // branch is the one currently checked out: use the working folder.
            var sourceFilePath = projectPath.AppendPart( "CKliTestModification.cs" );
            if( !File.Exists( sourceFilePath ) )
            {
                File.WriteAllText( sourceFilePath, "// Created" );
            }
            else
            {
                File.WriteAllText( sourceFilePath, File.ReadAllText( sourceFilePath ) + $"{Environment.NewLine}{DateTime.UtcNow}" );
            }
            Commands.Stage( git, "*" );
            git.Commit( commitMessage, context.Committer, context.Committer );
        }
    }

    /// <summary>
    /// Uses a "Cloned/" folder (that has been handled by another test) to create a new "Remote/" one.
    /// <list type="bullet">
    ///     <item>The source "Cloned/builderMethodName" must exist and must obviously be the result of a successful unit test.</item>
    ///     <item>The target "Remote/stack(testState)" must not already exist.</item>
    ///     <item>This removes the "origin" remote from all the repositories</item>
    ///     <item>The source "Cloned/builderMethodName" is cleared once done.</item>
    ///     <item>The "Remotes/Remotes.zip" must be updated thanks to the "ZipRemotes.ps1" script.</item>
    /// </list>
    /// </summary>
    /// <param name="builderMethodName">The name of the method that worked on the "Cloned/&lt;builderMethodName&gt;" to setup it. Example: "CKt_init_Async".</param>
    /// <param name="stackName">The stack name to consider. Example: "CKt".</param>
    /// <param name="testStateName">
    /// The state name for the new Remote. Example: "(initialized)" will create "CKt(initialized)" remote folder.
    /// Parentheses are required.
    /// </param>
    public static void CKliCreateRemoteFolderFromCloned( this IMonitorTestHelper helper, string builderMethodName, string stackName, string testStateName )
    {
        Throw.CheckArgument( !string.IsNullOrEmpty( testStateName ) && testStateName[0] == '(' && testStateName[^1] == ')' );
        var destination = TestHelper.CKliRemotesPath.AppendPart( stackName + testStateName );
        if( Directory.Exists( destination ) )
        {
            Throw.CKException( $"""
                Target directory already exists: '{destination}'.
                It must be explicitly deleted before calling this method:

                FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "{stackName + testStateName}" ) );

                """ );
        }
        var source = TestHelper.CKliClonedPath.AppendPart( builderMethodName ).AppendPart( stackName );
        var context = new CKliEnv( source );
        if( !StackRepository.OpenWorldFromPath( TestHelper.Monitor, context, out var stack, out var world, skipPullStack: true, withPlugins: false ) )
        {
            Throw.CKException( $"Unable to open cloned stack '{stackName}' from '{context.CurrentDirectory}'." );
        }

        var copyRoadmap = new List<(NormalizedPath, string)>() { (stack.StackWorkingFolder, stack.StackName + "-Stack") };
        try
        {
            var allRepos = world.GetAllDefinedRepo( TestHelper.Monitor );
            if( allRepos == null )
            {
                Throw.CKException( $"Unable to enumerate Repos in '{world.Name}'." );
            }
            foreach( var repo in allRepos )
            {
                if( !repo.GitRepository.CheckCleanCommit( TestHelper.Monitor ) )
                {
                    Throw.CKException( $"Repository '{repo.DisplayPath}' is dirty." );
                }
                repo.GitRepository.Repository.Network.Remotes.Remove( "origin" );
                copyRoadmap.Add( (repo.WorkingFolder, repo.WorkingFolder.LastPart) );
            }
            stack.GitRepository.Repository.Network.Remotes.Remove( "origin" );
        }
        finally
        {
            stack.Dispose();
        }

        foreach( var (path, name) in copyRoadmap )
        {
            var target = destination.AppendPart( name );
            Directory.CreateDirectory( target );
            FileUtil.CopyDirectory( new DirectoryInfo( path ), new DirectoryInfo( target ) );
        }
        TestHelper.InitializeClonedFolder( builderMethodName, clearStackRegistryFile: true );
    }

}
