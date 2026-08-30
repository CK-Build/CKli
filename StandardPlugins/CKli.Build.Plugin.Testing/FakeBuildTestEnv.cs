using CK.Core;
using CK.Testing;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using Shouldly;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli;

/// <summary>
/// Fake build helper. This use <see cref="BuildPlugin.SetBuilderFunction(BuilderFunction?)"/> to
/// install a global fake build function. Must be disposed once done.
/// </summary>
public sealed partial class FakeBuildTestEnv : IDisposable
{
    readonly IMonitorTestHelper _helper;
    readonly NormalizedPath _path;
    readonly BuilderFunction _previous;
    readonly GitFileProviderCache _fileProviderCache;
    int _disposed;

    internal FakeBuildTestEnv( IMonitorTestHelper helper,
                               NormalizedPath path,
                               BuilderFunction previous )
    {
        _helper = helper;
        _path = path;
        _previous = previous;
        _fileProviderCache = new GitFileProviderCache();

    }

    /// <summary>
    /// Gets the Working Test Folder path.
    /// </summary>
    public NormalizedPath Path => _path;

    /// <summary>
    /// Gets a centralized cache for <see cref="INormalizedFileProvider"/>.
    /// </summary>
    public GitFileProviderCache FileProviderCache => _fileProviderCache;

    ///// <summary>
    /// <summary>
    /// Creates a new stack in <see cref="Path"/>/bare/<paramref name="name"/> folder and returns
    /// a <see cref="CKliTestHelperExtensions.RemotesFolder"/> that can be used to clone it.
    /// </summary>
    /// <param name="name">The stack name. Must not end with "-Stack".</param>
    /// <returns>The remotes folder.</returns>
    public async Task<FakeBuildStack> CreateStackAsync( string stackName = "Test",
                                                        Action<IActivityMonitor, NormalizedPath, XElement>? pluginConfigurationEditor = null,
                                                        bool allowDuplicateStack = false,
                                                        bool privateStack = false )
    {
        Throw.CheckArgument( !stackName.EndsWith( "-Stack" ) );
        // We want to use the real "ckli create <url>" command here.
        var rootContext = new CKliEnv( _path, screen: new StringScreen(), findCurrentStackPath: false );

        var barePath = _path.AppendPart( "bare" );
        (await CKliCommands.ExecAsync( _helper.Monitor, rootContext, "create", new Uri( barePath.AppendPart( stackName + "-Stack" ) ), "--ignore-parent-stack" ).ConfigureAwait( false )).ShouldBeTrue();
        var clonedPath = _path.AppendPart( stackName );
        Throw.DebugAssert( "The 'ckli create' has cloned the stack (with only the .Public in it).", Directory.Exists( clonedPath ) );
        // We destroy the created clone: we need to clone it with the RemotesFolder.CloneAsync() that handles <Plugins> configurations
        // from the actual World plugins.
        FileHelper.DeleteFolder( _helper.Monitor, clonedPath );

        var remotes = new CKliTestHelperExtensions.RemotesFolder( barePath, stackName );
        var defaultWorldContext = await remotes.CloneAsync( _path, allowDuplicateStack, pluginConfigurationEditor, privateStack ).ConfigureAwait( false );

        return new FakeBuildStack( this, _helper, remotes, defaultWorldContext, privateStack );
    }

    /// <summary>
    /// Restores the real build function (<see cref="BuildPlugin.SetBuilderFunction(BuilderFunction?)"/>).
    /// </summary>
    public void Dispose()
    {
        if( Interlocked.Increment( ref _disposed ) == 1 )
        {
            CKliBuildPluginTestHelperExtensions.OnDispose( _previous, _helper );
        }
    }

}
