using CK.Core;
using CK.Testing;
using CKli.ArtifactHandler.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

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
        var previous = BuildPlugin.SetBuilderFunction( FakeBuildAsync );
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

    /// <summary>
    /// Fake <see cref="BuilderFunction"/>: not a single "dotnet" process is started here.
    /// <list type="bullet">
    ///     <item>
    ///     The working folder is left as-is: the <paramref name="buildCommit"/> is not checked out, its content
    ///     is read from the Git tree by the <see cref="ShallowSolutionPlugin"/>.
    ///     </item>
    ///     <item>
    ///     "dotnet build" does nothing and "dotnet test" always succeeds (but the tested content is recorded
    ///     by <see cref="RepoBuilder.SetTestRun(IActivityMonitor, Commit)"/> just like the real build does).
    ///     </item>
    ///     <item>
    ///     "dotnet pack" writes a minimal ".nupkg" (see <see cref="WriteFakePackage"/>) for each packable project of
    ///     the solution and "dotnet package list" is replaced by the &lt;PackageReference&gt; found in the project files.
    ///     </item>
    ///     <item>
    ///     The "Deployment" assets are not faked: <see cref="BuildContentInfo.AssetFileNames"/> is always empty.
    ///     </item>
    /// </list>
    /// Everything else (publishing to the local NuGet feed and tagging the built commit) is the real thing.
    /// </summary>
    static Task<BuildResult?> FakeBuildAsync( IActivityMonitor monitor,
                                              CKliEnv context,
                                              VersionTagInfo versionInfo,
                                              Commit buildCommit,
                                              bool runTest,
                                              RepoBuilder repoBuilder,
                                              CommitBuildInfo buildInfo,
                                              CancellationToken cancellation )
    {
        return Task.FromResult( FakeBuild( monitor, context, buildCommit, runTest, repoBuilder, buildInfo, cancellation ) );
    }

    static BuildResult? FakeBuild( IActivityMonitor monitor,
                                   CKliEnv context,
                                   Commit buildCommit,
                                   bool runTest,
                                   RepoBuilder repoBuilder,
                                   CommitBuildInfo buildInfo,
                                   CancellationToken cancellation )
    {
        var repo = buildInfo.Repo;
        using var gLog = monitor.OpenTrace( $"Fake build for '{buildInfo}'." );

        if( cancellation.IsCancellationRequested ) return null;

        var shallow = repo.World.GetRequiredPlugin<ShallowSolutionPlugin>( monitor );
        var artifactHandler = repo.World.GetRequiredPlugin<ArtifactHandlerPlugin>( monitor );
        if( shallow == null || artifactHandler == null ) return null;

        // The whole point of this fake: we read the commit's tree instead of checking it out and building it.
        // The solution is required here, just like the real build requires it to compile anything.
        var solution = shallow.GetRequiredContent( monitor, repo, buildCommit, useWorkingFolder: false );
        if( solution == null ) return null;

        // Instead of "dotnet package list --format json", the consumed packages are the <PackageReference> of
        // the projects and of their reachable "Directory.Build.props".
        // GitSolutionContent.Consumed is a set: ordering it gives the strictly sorted array that BuildContentInfo requires.
        var consumed = solution.Consumed.Order().ToImmutableArray();

        // Instead of "dotnet pack", one minimal package per packable project.
        // A project with no <IsPackable> is packable: this is the HotGraph convention.
        var packageIds = solution.Projects.Where( p => p.IsPackable is not false )
                                          .Select( p => p.Name )
                                          .Distinct( StringComparer.OrdinalIgnoreCase );

        // Instead of "dotnet test": tests always pass. Recording the content sha is what makes the
        // "tests already ran on this content" optimization observable by tests.
        if( runTest )
        {
            repoBuilder.SetTestRun( monitor, buildCommit );
        }

        // Same temporary output folder pattern as the real RepoBuilder.BuildAsync.
        var outputPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath() + "CKliFakeBuild", null, DateTime.UtcNow );
        try
        {
            foreach( var packageId in packageIds )
            {
                WriteFakePackage( outputPath, packageId, buildInfo.Version );
            }
            if( cancellation.IsCancellationRequested ) return null;

            // From here, this is the real build: the packages are published in the local feed and the
            // version tag with its BuildContentInfo annotation is set on the built commit.
            var repoArtifact = artifactHandler.Get( monitor, repo );
            if( !repoArtifact.PublishToNuGetLocalFeed( monitor, buildInfo.Version, outputPath, out var produced ) )
            {
                return null;
            }
            var content = new BuildContentInfo( consumed, produced, assetFileNames: [] );
            var (tag, version) = buildInfo.ApplyReleaseBuildTag( monitor, context, content );
            if( tag == null )
            {
                return null;
            }
            Throw.DebugAssert( version != null );
            return new BuildResult( repo, tag, version, content, assetsFolder: default, skippedBuild: false );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While faking the build of '{buildInfo}'.", ex );
            return null;
        }
        finally
        {
            FileHelper.DeleteFolder( monitor, outputPath );
        }
    }

    /// <summary>
    /// Writes a "&lt;packageId&gt;.&lt;version&gt;.nupkg" file in <paramref name="outputPath"/>: a zip archive with a
    /// single ".nuspec" entry, exactly as basic as the CK.CanaryPackage (no dependency, no content).
    /// </summary>
    /// <param name="outputPath">The existing folder into which the package must be written.</param>
    /// <param name="packageId">The produced package identifier.</param>
    /// <param name="version">The built version.</param>
    static void WriteFakePackage( string outputPath, string packageId, SVersion version )
    {
        // The file name pattern matters: RepoArtifactInfo.PublishToNuGetLocalFeed and
        // ArtifactHandlerPlugin.HasAllArtifacts both rely on "<packageId>.<version>.nupkg".
        var filePath = Path.Combine( outputPath, $"{packageId}.{version}.nupkg" );
        using( var file = File.Create( filePath ) )
        using( var zip = new ZipArchive( file, ZipArchiveMode.Create ) )
        using( var w = new StreamWriter( zip.CreateEntry( $"{packageId}.nuspec", CompressionLevel.Optimal ).Open(), Encoding.UTF8 ) )
        {
            w.Write( $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{packageId}</id>
                    <version>{version}</version>
                    <authors>CKli.Build.Plugin.Testing</authors>
                    <description>Zero-dependency fake package produced by the CKli fake build.</description>
                  </metadata>
                </package>
                """ );
        }
    }
}
