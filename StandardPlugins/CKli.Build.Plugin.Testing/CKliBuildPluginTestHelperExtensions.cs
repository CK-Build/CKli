using CK.Core;
using CK.Testing;
using CKli.Build.Plugin;
using CKli.Core;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Extends <see cref="IMonitorTestHelper"/> with <see cref="CKliCreateFakeBuildTestEnvAsync(IMonitorTestHelper, string?, bool)"/>.
/// </para>
/// </summary>
public static partial class CKliBuildPluginTestHelperExtensions
{
    static int _fileSystemWritePATCount;

    /// <summary>
    /// Creates a <see cref="FakeBuildTestEnv"/> for the current method (by default).
    /// </summary>
    /// <param name="helper">This helper.</param>
    /// <param name="methodTestName">The method test name.</param>
    /// <param name="clearStackRegistryFile">True to clear the stack registry (<see cref="StackRepository.ClearRegistry"/>).</param>
    /// <returns>A test environment that must be disposed once done.</returns>
    public static async Task<FakeBuildTestEnv> CKliCreateFakeBuildTestEnvAsync( this IMonitorTestHelper helper,
                                                                                [CallerMemberName] string? methodTestName = null,
                                                                                bool clearStackRegistryFile = true )
    {
        NormalizedPath wtfPath = helper.PrepareWorkingTestFolder( methodTestName, clearStackRegistryFile );
        if( Interlocked.Increment( ref _fileSystemWritePATCount ) == 1 )
        {
            helper.SetFileSystemWritePAT();
        }
        // The environment installs its own FakeBuildAsync (an instance method) in its constructor.
        return new FakeBuildTestEnv( helper, wtfPath );
    }

    internal static void OnDispose( BuilderFunction previous, IMonitorTestHelper helper )
    {
        BuildPlugin.SetBuilderFunction( previous );
        if( Interlocked.Decrement( ref _fileSystemWritePATCount ) == 0 )
        {
            helper.RemoveFileSystemWritePAT();
        }
    }

}
