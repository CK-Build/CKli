
using CK.Core;
using CK.Testing;
using CKli.Core;
using System.Linq;
using System.Runtime.Loader;
using static CK.Testing.MonitorTestHelper;

namespace CKli;

/// <summary>
/// Provides extension members to <see cref="IMonitorTestHelper"/>.
/// <para>
/// This class has a static initializer that setups the CKli context so it can be used
/// in NUnit context.
/// </para>
/// </summary>
public static class CKliTestHelperExtensions
{
    readonly static NormalizedPath _remotesPath;
    readonly static NormalizedPath _barePath;
    readonly static NormalizedPath _clonedPath;
    readonly static WorldName _worldName;

    static CKliTestHelperExtensions()
    {
        Throw.CheckState( TestHelper.TestProjectFolder.Path.EndsWith( "/Tests/Plugins.Tests" ) );

        _remotesPath = TestHelper.TestProjectFolder.AppendPart( "Remotes" );
        _barePath = _remotesPath.AppendPart( "bare" );
        _clonedPath = TestHelper.TestProjectFolder.AppendPart( "Cloned" );

        var pluginFolderName = TestHelper.TestProjectFolder.Parts[^3];
        Throw.CheckState( pluginFolderName.Contains( "-Plugins" ) );
        int idx = pluginFolderName.IndexOf( "-Plugins" );
        var stackName = pluginFolderName.Substring( 0, idx );
        var ltsName = pluginFolderName.Substring( 0, idx + 8 );
        _worldName = new WorldName( stackName, ltsName );

        // The CKliRootEnv.AppLocalDataPath is: Environment.SpecialFolder.LocalApplicationData/CKli-Test-StackName-Plugins[LTSName].
        CKliRootEnv.Initialize( $"Test-{pluginFolderName}",
                                screen: new StringScreen(),
                                findCurrentStackPath: false );
        // Single so that this throws if naming change.
        var nunitLoadContext = AssemblyLoadContext.All.FirstOrDefault( c => c.GetType().Name == "TestAssemblyLoadContext" );
        if( nunitLoadContext == null )
        {
            Throw.InvalidOperationException( """
                NUnit test adapter doesn't use AssemblyLoadContext named 'TestAssemblyLoadContext'.
                This must be investigated.
                """ );
        }
        CKli.Loader.PluginLoadContext.Initialize( nunitLoadContext );
        World.PluginLoader = CKli.Loader.PluginLoadContext.Load;
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
        /// remote repositories are cloned and used by tests.
        /// </summary>
        public NormalizedPath CKliClonedPath => _clonedPath;

        /// <summary>
        /// Gets the World name that contains the plugins (and these tests).
        /// </summary>
        public WorldName CKliWorldName => _worldName;
    }
}
