using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

public sealed partial class FakeBuildTestEnv
{

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
    ///     Nothing is restored, so there is no NuGet resolution to read the transitive packages from: they
    ///     are the ones a test declared on <see cref="FakeBuildRepo.TransitivePackages"/>, and
    ///     <see cref="BuildContentInfo.HasTransitive"/> is false when it declared none. Defaulting to an empty
    ///     set instead would assert that a restore brings nothing, which is a different and false statement.
    ///     </item>
    ///     <item>
    ///     The "Deployment" assets are not faked: <see cref="BuildContentInfo.AssetFileNames"/> is always empty.
    ///     </item>
    /// </list>
    /// Everything else (publishing to the local NuGet feed and tagging the built commit) is the real thing.
    /// </summary>
    internal Task<BuildResult?> FakeBuildAsync( IActivityMonitor monitor,
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

    BuildResult? FakeBuild( IActivityMonitor monitor,
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

        // A test can make a repository's build fail through FakeBuildRepo.FailBuild. This bails out before
        // anything is written - in particular before ApplyReleaseBuildTag - which is what a build that fails
        // to compile does. Failing any solution of the roadmap is enough to stop the RoadmapExecutor from
        // promoting the "building/" tags of the ones that DID succeed.
        var declaring = FindFakeBuildRepo( repo );
        if( declaring != null && declaring.FailBuild )
        {
            monitor.Error( $"Fake build failure requested by FakeBuildRepo.FailBuild for '{buildInfo}'." );
            return null;
        }

        var shallow = repo.World.GetRequiredPlugin<ShallowSolutionPlugin>( monitor );
        var artifactHandler = repo.World.GetRequiredPlugin<ArtifactHandlerPlugin>( monitor );
        if( shallow == null || artifactHandler == null ) return null;

        // The whole point of this fake: we read the commit's tree instead of checking it out and building it.
        // The solution is required here, just like the real build requires it to compile anything.
        var solution = shallow.GetRequiredContent( monitor, repo, buildCommit, useWorkingFolder: false );
        if( solution == null ) return null;

        // Instead of "dotnet package list --include-transitive --format json", the consumed packages are the
        // <PackageReference> of the projects and of their reachable "Directory.Build.props". The transitive
        // ones can only be declared: see the remarks above.
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
        var outputPath = FileUtil.CreateUniqueTimedFolder( System.IO.Path.GetTempPath() + "CKliFakeBuild", null, DateTime.UtcNow );
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
            var content = new BuildContentInfo( consumed, produced, assetFileNames: [], GetTransitivePackages( repo ) );
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

    // A test declares things about a repository's fake build on its FakeBuildRepo (TransitivePackages,
    // FailBuild): this dictionary is the join between the CKli Repo the build is given and the FakeBuildRepo
    // that declares. It belongs to this environment - the fake build function is this instance's method - so
    // an entry of another test cannot be reached at all.
    // The key is still the origin url (not the name) because it is what a SECOND CLONE of the same remotes
    // shares with the first one, and a coworking test builds from both.
    // It stays concurrent: the roadmap builds up to --max-dop solutions at once.
    readonly ConcurrentDictionary<string, FakeBuildRepo> _fakeBuildRepos = new( StringComparer.Ordinal );

    internal void RegisterFakeBuildRepo( Uri originUrl, FakeBuildRepo repo )
    {
        _fakeBuildRepos[originUrl.AbsoluteUri] = repo;
    }

    // An unknown repository - nothing was ever declared, or the Repo comes from no FakeBuildRepo at all.
    FakeBuildRepo? FindFakeBuildRepo( Repo repo )
    {
        return _fakeBuildRepos.GetValueOrDefault( repo.OriginUrl.AbsoluteUri );
    }

    // Leaves the transitive packages unrecorded for an unknown repository, which is what every fake build did
    // before this seam existed.
    ImmutableArray<PackageInstance> GetTransitivePackages( Repo repo )
    {
        var fake = FindFakeBuildRepo( repo );
        return fake != null ? fake.TransitivePackages : default;
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
        var filePath = System.IO.Path.Combine( outputPath, $"{packageId}.{version}.nupkg" );
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
