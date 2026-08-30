using CK.Core;
using CK.Testing;
using CKli.ArtifactHandler.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using Shouldly;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli;

/// <summary>
/// Extends <see cref="IMonitorTestHelper"/> with <see cref="CKliCreateFakeBuildTestEnv(IMonitorTestHelper, string?, bool)"/>.
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
    public static async Task<FakeBuildTestEnv> CKliCreateFakeBuildTestEnv( this IMonitorTestHelper helper,
                                                                           [CallerMemberName] string? methodTestName = null,
                                                                           bool clearStackRegistryFile = true )
    {
        NormalizedPath wtfPath = helper.PrepareWorkingTestFolder( methodTestName, clearStackRegistryFile );
        if( Interlocked.Increment( ref _fileSystemWritePATCount ) == 1 )
        {
            helper.SetFileSystemWritePAT();
        }
        var previous = BuildPlugin.SetBuilderFunction( FakeBuild );
        return new FakeBuildTestEnv( helper, wtfPath, previous );
    }

    internal static void OnDispose( BuilderFunction previous, IMonitorTestHelper helper )
    {
        BuildPlugin.SetBuilderFunction( previous );
        if( Interlocked.Decrement( ref _fileSystemWritePATCount ) == 0 )
        {
            helper.RemoveFileSystemWritePAT();
        }
    }

    private static async Task<BuildResult?> FakeBuild( IActivityMonitor monitor,
                                                       CKliEnv context,
                                                       VersionTagInfo versionInfo,
                                                       Commit buildCommit,
                                                       bool runTest,
                                                       RepoBuilder repoBuilder,
                                                       CommitBuildInfo buildInfo,
                                                       CancellationToken cancellation )
    {
        throw new NotImplementedException();
    }
}
