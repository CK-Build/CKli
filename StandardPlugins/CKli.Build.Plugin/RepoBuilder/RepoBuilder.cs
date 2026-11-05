using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;

using LibGit2Sharp;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CKli.Build.Plugin;

/// <summary>
/// Concrete base class that can be specialized.
/// <para>
/// The <see cref="RepositoryBuilderPlugin.Create(IActivityMonitor, Repo)"/> acts as an abstract factory:
/// the actual RepoBuilder may differ for each Repo one day.
/// </para>
/// </summary>
public class RepoBuilder : RepoInfo
{
    /// <summary>
    /// Tags the error that concludes a failed "dotnet build" step of <see cref="BuildAsync"/>.
    /// </summary>
    public static readonly CKTrait DotNetBuildFailedTag = ActivityMonitor.Tags.Register( "DotNetBuildFailed" );

    /// <summary>
    /// Tags the error that concludes a failed "dotnet test" step of <see cref="BuildAsync"/>.
    /// </summary>
    public static readonly CKTrait DotNetTestFailedTag = ActivityMonitor.Tags.Register( "DotNetTestFailed" );

    /// <summary>
    /// Tags the error that concludes a failed "dotnet pack" step of <see cref="BuildAsync"/>.
    /// </summary>
    public static readonly CKTrait DotNetPackFailedTag = ActivityMonitor.Tags.Register( "DotNetPackFailed" );

    /// <summary>
    /// Gets the failed step that the tags of a concluding error name ("dotnet build", "dotnet test" or
    /// "dotnet pack"), or null when they name none: a display that already names the repository says
    /// the step rather than repeating that error.
    /// </summary>
    /// <param name="tags">The tags of an error.</param>
    /// <returns>The failed step or null.</returns>
    public static string? GetFailedStep( CKTrait tags ) => tags.Overlaps( DotNetBuildFailedTag )
                                                            ? "dotnet build"
                                                            : tags.Overlaps( DotNetTestFailedTag )
                                                                ? "dotnet test"
                                                                : tags.Overlaps( DotNetPackFailedTag )
                                                                    ? "dotnet pack"
                                                                    : null;

    readonly RepositoryBuilderPlugin _repositoryBuilder;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly RepoArtifactInfo _repoArtifact;

    /// <summary>
    /// Initializes a new <see cref="RepoBuilder"/>.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="repositoryBuilder">The factory.</param>
    /// <param name="artifactHandler">The artifact handler plugin.</param>
    /// <param name="repoArtifact">The repository artifact info.</param>
    public RepoBuilder( Repo repo, RepositoryBuilderPlugin repositoryBuilder, ArtifactHandlerPlugin artifactHandler, RepoArtifactInfo repoArtifact )
        : base( repo )
    {
        _repositoryBuilder = repositoryBuilder;
        _artifactHandler = artifactHandler;
        _repoArtifact = repoArtifact;
    }

    /// <summary>
    /// Checks whether tests has already successfully run on the commit.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="commit">The commit.</param>
    /// <returns>True if the tests have already successfully run, false otherwise.</returns>
    public bool HasTestRun( IActivityMonitor monitor, Commit commit )
    {
        string testKey = commit.Tree.Sha;
        return _repositoryBuilder._shaTestRunCache.Contains( monitor, testKey );
    }

    /// <summary>
    /// Records that tests have successfully run on the commit's content: <see cref="HasTestRun(IActivityMonitor, Commit)"/>
    /// will return true for any commit with the same content.
    /// <para>
    /// The build plugin calls this once a build that ran the tests succeeded: a <see cref="BuilderFunction"/> doesn't.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="commit">The commit on which the tests have run.</param>
    public void SetTestRun( IActivityMonitor monitor, Commit commit )
    {
        _repositoryBuilder._shaTestRunCache.Add( monitor, commit.Tree.Sha );
    }

    /// <summary>
    /// Build, test, package and generate the optional Deployment assets of the repository.
    /// On success, the <see cref="BuildOutput"/> contains the consumed NuGet packages, the folder of the
    /// produced ones and the published deployment assets.
    /// <para>
    /// The "nuget.config" file is restored but the working folder is left as the build left it: the caller
    /// handles the impacts of the build, resets the working folder, publishes the packages, tags the version and
    /// deletes the <see cref="BuildOutput.PackagesFolder"/>.
    /// </para>
    /// <para>
    /// This can throw: it is up to the caller to catch (or not) exceptions.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">Minimal CKli context.</param>
    /// <param name="buildInfo">The build info.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The build output on success, null otherwise.</returns>
    public async Task<BuildOutput?> BuildAsync( IActivityMonitor monitor,
                                                CKliEnv context,
                                                CommitBuildInfo buildInfo,
                                                bool runTest,
                                                CancellationToken cancellation )
    {
        Throw.CheckState( "Repository must not be dirty when calling build.", !Repo.GitRepository.GetSimpleStatusInfo().IsDirty );
        Throw.CheckArgument( buildInfo.Repo == Repo );

        // Calling "dotnet build --source <localFeed>" fails with:
        //      error NU1100: Unable to resolve 'CKt.Core (>= 1.0.0)' for 'net8.0'.
        //      PackageSourceMapping is enabled, the following source( s ) were not considered: <localFeed>
        //
        // Even when nuget.config doesn't contain any <packageSourceMapping>...
        // 
        // Before building, we modify the nuget.config (this helper ensures that <packageSourceMapping> exists
        // with all the existing sources plus the one we add).
        //
        monitor.Trace( "Add the local feed to the 'nuget.config' file." );
        var nugetConfigPath = Repo.WorkingFolder.AppendPart( "nuget.config" );
        // The modified file is restored after the build: it must not appear among the impacts of the build.
        var originalNuGetConfig = File.Exists( nugetConfigPath ) ? File.ReadAllBytes( nugetConfigPath ) : null;

        var configRoot = NuGetHelper.GetConfigurationRoot( monitor, nugetConfigPath );
        if( configRoot == null ) return null;

        var localFeed = _repoArtifact.LocalFeedNuGetPath;
        if( !NuGetHelper.SetOrRemoveNuGetSource( monitor, configRoot, "local-feed", localFeed ) )
        {
            return null;
        }

        if( !_artifactHandler.ApplyConfiguredNuGetFeeds( monitor, configRoot, out var actions, context.SecretsStore ) )
        {
            return null;
        }

        if( actions != null )
        {
            monitor.Warn( $"""
                The 'nuget.config' file must be updated ({nugetConfigPath}):
                {actions.Concatenate( Environment.NewLine )}
                """ );
        }

        if( cancellation.IsCancellationRequested ) return null;

        // Temporary output folder for artifacts: on success, it belongs to the caller.
        var outputPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath() + "CKliBuild", null, DateTime.UtcNow );
        BuildOutput? output = null;
        try
        {
            try
            {
                XmlHelper.SafeSave( configRoot, nugetConfigPath, SaveOptions.DisableFormatting );
                var (success, hooked) = await _repositoryBuilder.RaiseOnCoreBuildAsync( monitor, context, buildInfo, outputPath, runTest, cancellation ).ConfigureAwait( false );
                if( !success || hooked != null )
                {
                    return output = hooked;
                }
                if( !DotNetBuildTestPack( monitor, buildInfo, runTest, outputPath, cancellation )
                    || cancellation.IsCancellationRequested
                    || !BuildResult.GetConsumedPackages( monitor,
                                                         Repo,
                                                         buildInfo.ToString(),
                                                         out var consumedPackages,
                                                         out var transitivePackages ) )
                {
                    return null;
                }
                RestoreNuGetConfig( nugetConfigPath, originalNuGetConfig );
                var deploymentFolder = Repo.WorkingFolder.AppendPart( ArtifactHandlerPlugin.DeployFolderName );
                if( !HandleDeployAssets( monitor, deploymentFolder, buildInfo.Version, out var assetsFolder, out var assetFileNames ) )
                {
                    return null;
                }
                return output = new BuildOutput( outputPath, consumedPackages, transitivePackages, assetsFolder, assetFileNames );
            }
            finally
            {
                RestoreNuGetConfig( nugetConfigPath, originalNuGetConfig );
            }
        }
        finally
        {
            if( output == null || output.PackagesFolder != outputPath )
            {
                FileHelper.DeleteFolder( monitor, outputPath );
            }
        }

        static void RestoreNuGetConfig( NormalizedPath nugetConfigPath, byte[]? originalNuGetConfig )
        {
            if( originalNuGetConfig == null )
            {
                if( File.Exists( nugetConfigPath ) ) File.Delete( nugetConfigPath );
            }
            else
            {
                File.WriteAllBytes( nugetConfigPath, originalNuGetConfig );
            }
        }
    }

    bool HandleDeployAssets( IActivityMonitor monitor,
                             NormalizedPath deploymentFolder,
                             SVersion version,
                             out NormalizedPath assetsFolder,
                             out ImmutableArray<string> fileNames )
    {
        assetsFolder = default;
        fileNames = [];
        if( !Directory.Exists( deploymentFolder ) )
        {
            // No Deployment assets.
            monitor.Trace( $"No '{ArtifactHandlerPlugin.DeployFolderName}' folder in '{Repo.DisplayPath}'." );
            return true;
        }
        using var gLog = monitor.OpenInfo( $"Handling '{ArtifactHandlerPlugin.DeployFolderName}' folder in '{Repo.DisplayPath}'." );
        try
        {
            var generateFileApp = deploymentFolder.AppendPart( "GenerateAssets.cs" );
            if( !File.Exists( generateFileApp ) )
            {
                // Not an error.
                monitor.Warn( $"Ignoring '{deploymentFolder}' because no 'GenerateAssets.cs' file exists." );
                return true;
            }
            var repoAssetsFolder = deploymentFolder.AppendPart( ArtifactHandlerPlugin.DeployAssetsName );
            if( Directory.Exists( repoAssetsFolder ) )
            {
                monitor.Trace( $"Cleaning up '{repoAssetsFolder}'." );
                if( !FileHelper.DeleteFolder( monitor, repoAssetsFolder ) )
                {
                    return false;
                }
            }
            var gitignoreAssets = deploymentFolder.AppendPart( ".gitignore" );
            if( !File.Exists( gitignoreAssets ) )
            {
                monitor.Trace( $"Missing 'Deployment/.gitignore' file. Initializing it." );
                File.WriteAllText( gitignoreAssets, "Assets/" );
            }
            Directory.CreateDirectory( repoAssetsFolder );
            var e = ProcessRunner.RunProcess( monitor.ParallelLogger,
                                              "dotnet",
                                              $"run GenerateAssets.cs -- {version}",
                                              deploymentFolder );
            if( e != 0 )
            {
                monitor.Error( $"Running '{generateFileApp}' failed with code '{e}'." );
                return false;
            }
            return _repoArtifact.PublishGeneratedAssets( monitor, version, out assetsFolder, out fileNames );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While handling '{deploymentFolder}' creation.", ex );
            return false;
        }
    }

    /// <summary>
    /// Helper that calls <see cref="DotNetBuild"/>, <see cref="DotNetTest"/> and <see cref="DotNetPack"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="buildInfo">The build information.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <param name="packOutputPath">Destination folder where the artifact files must be created.</param>
    /// <param name="cancellation">The cancellation token to consider.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected virtual bool DotNetBuildTestPack( IActivityMonitor monitor,
                                                CommitBuildInfo buildInfo,
                                                bool runTest,
                                                string packOutputPath,
                                                CancellationToken cancellation )
    {
        bool buildSuccess = true;
        bool testSuccess = true;
        bool packSuccess = true;
        if( !cancellation.IsCancellationRequested
            && (buildSuccess = DotNetBuild( monitor,
                                            buildInfo.Version,
                                            buildInfo.InformationalVersion,
                                            buildInfo.FileVersion,
                                            buildInfo.ReleaseConfiguration,
                                            cancellation ))
            && (!runTest || (testSuccess = DotNetTest( monitor, buildInfo.ReleaseConfiguration, cancellation )))
            && (packSuccess = DotNetPack( monitor, buildInfo.Version, buildInfo.ReleaseConfiguration, packOutputPath, cancellation )) )
        {
            return true;
        }
        if( !buildSuccess )
        {
            monitor.Error( DotNetBuildFailedTag, $"Dotnet build fails for '{buildInfo.Repo.DisplayPath}'." );
        }
        else if( !testSuccess )
        {
            monitor.Error( DotNetTestFailedTag, $"Dotnet test fails for '{buildInfo.Repo.DisplayPath}'." );
        }
        else if( !packSuccess )
        {
            monitor.Error( DotNetPackFailedTag, $"Dotnet pack fails for '{buildInfo.Repo.DisplayPath}'." );
        }
        // Cancellation has no error message.
        return false;
    }

    /// <summary>
    /// Helper that calls "dotnet build".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to build.</param>
    /// <param name="informationalVersion">The informational version to set (see <see cref="InformationalVersion"/>).</param>
    /// <param name="fileVersion">The windows file version. See <see cref="CommitBuildInfo.FileVersion"/>.</param>
    /// <param name="release">False to use Debug build configuration.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetBuild( IActivityMonitor monitor,
                                SVersion version,
                                string informationalVersion,
                                string fileVersion,
                                bool release,
                                CancellationToken cancellation )
    {
        // Currently, we always set ContinuousIntegrationBuild.
        // This may impact the SourceRoot for SourceLink: this can allow a local UX that debugs straight to the right source file.
        // If not, we may differentiate "pure local" and "publishable" builds so that local builds can source link to the local file paths.
        return Repo.RunDotnet( monitor, $"""
            build -tl:off --nologo --no-incremental -c {(release ? "Release" : "Debug")
            } /p:Version="{version
            }" /p:InformationalVersion="{informationalVersion
            }" /p:FileVersion="{fileVersion
            }" /p:ContinuousIntegrationBuild=true
            """,
            cancellation: cancellation );
    }

    /// <summary>
    /// The "--blame-hang-timeout" of <see cref="DotNetTest"/>: a test that runs longer than this is considered hung.
    /// The test host is then killed (after a hang dump is collected) and the tests fail, instead of blocking the
    /// whole roadmap forever.
    /// </summary>
    public const string TestHangTimeout = "20m";

    /// <summary>
    /// Helper that calls "dotnet test" (with a <see cref="TestHangTimeout"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="release">Whether the build is in debug or in release.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on tests success, false otherwise.</returns>
    protected bool DotNetTest( IActivityMonitor monitor, bool release, CancellationToken cancellation )
    {
        // The test run is recorded by the build plugin once the build succeeded (see SetTestRun).
        return Repo.RunDotnet( monitor, $"test -tl:off -c {(release ? "Release" : "Debug")} --nologo --no-build --blame-hang-timeout {TestHangTimeout}", cancellation: cancellation );
    }

    /// <summary>
    /// Helper that calls "dotnet pack".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to pack.</param>
    /// <param name="release">Whether the build is in debug or in release.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetPack( IActivityMonitor monitor, SVersion version, bool release, string outputPath, CancellationToken cancellation )
    {
        return Repo.RunDotnet( monitor, $"""
            pack -tl:off /p:Version={version} /p:ContinuousIntegrationBuild=true -c {(release ? "Release" : "Debug")} --nologo --no-build -o "{outputPath}" 
            """, cancellation: cancellation );
    }


}


