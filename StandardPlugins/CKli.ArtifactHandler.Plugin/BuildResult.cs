using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Captures the result of a successful repository build.
/// This result can have a true <see cref="SkippedBuild"/> (the commit to build was
/// already built).
/// <para>
/// This is not fully immutable: calling <see cref="CommitBuilding"/> updates
/// the <see cref="Version"/>, the <see cref="VersionTag"/> and the repository.
/// </para>
/// </summary>
public sealed partial class BuildResult
{
    readonly Repo _repo;
    readonly BuildContentInfo _buildContentInfo;
    readonly NormalizedPath _assetsFolder;
    readonly bool _skippedBuild;
    Tag _versionTag;
    SVersion _version;

    /// <summary>
    /// Initializes a result.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="versionTag">The version tag.</param>
    /// <param name="version">The built or already built version.</param>
    /// <param name="content">Existing content info.</param>
    /// <param name="assetsFolder">
    /// Assets folder is "$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;".
    /// <see cref="NormalizedPath.IsEmptyPath"/> if there is no <see cref="BuildContentInfo.AssetFileNames"/>.
    /// </param>
    /// <param name="skippedBuild">Whether the build has been skipped (the version is up to date and artefacts are available).</param>
    public BuildResult( Repo repo,
                        Tag versionTag, 
                        SVersion version,
                        BuildContentInfo content,
                        NormalizedPath assetsFolder,
                        bool skippedBuild )
    {
        Throw.CheckArgument( assetsFolder.IsEmptyPath == content.AssetFileNames.IsEmpty );

        Throw.CheckArgument( (version.IsLocal() && versionTag.CanonicalName.StartsWith("refs/tags/local/", StringComparison.Ordinal ))
                             || (version.IsBuilding() && versionTag.CanonicalName.StartsWith( "refs/tags/building/", StringComparison.Ordinal ))
                             || (!version.IsBuildingOrLocal() && versionTag.CanonicalName == $"refs/tags/v{version}") );

        _repo = repo;
        _versionTag = versionTag;
        _version = version;
        _buildContentInfo = content;
        _assetsFolder = assetsFolder;
        _skippedBuild = skippedBuild;
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the version built.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the commit's version tag.
    /// <para>
    /// This tag is synchronized with this <see cref="Version"/> and may be a mere (published) version or have
    /// a "building/" or "local/" prefix.
    /// </para>
    /// </summary>
    public Tag VersionTag => _versionTag;

    /// <summary>
    /// Gets whether the build has been skipped (the release database
    /// knows this release and all artifacts are already available locally).
    /// </summary>
    public bool SkippedBuild => _skippedBuild;

    /// <summary>
    /// Gets the build content.
    /// </summary>
    public BuildContentInfo Content => _buildContentInfo;

    /// <summary>
    /// Gets whether this result has no produced NuGet packages nor asset files.
    /// </summary>
    public bool IsEmpty => _assetsFolder.IsEmptyPath && _buildContentInfo.Produced.IsEmpty;

    /// <summary>
    /// Gets the assets folder in ""$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;".
    /// <see cref="NormalizedPath.IsEmptyPath"/> if there is no assets.
    /// </summary>
    public NormalizedPath AssetsFolder => _assetsFolder;

    /// <summary>
    /// Gets the <see cref="BuildContentInfo.Produced"/> with this <see cref="Version"/>.
    /// </summary>
    public IEnumerable<PackageInstance> Produced => _buildContentInfo.Produced.Select( p => new PackageInstance( p, _version ) );

    /// <summary>
    /// Updates <see cref="VersionTag"/> and <see cref="Version"/> by calling <see cref="CommitBuilding(Repo, Tag, SVersion)"/>.
    /// <para>
    /// This must be called only once.
    /// </para>
    /// </summary>
    public void CommitBuilding() => (_versionTag, _version) = CommitBuilding( _repo, _versionTag, _version );

    /// <summary>
    /// The <paramref name="buildingVersionTag"/> with a "refs/tags/building/" prefix is updated to have a "refs/tags/local/" prefix.
    /// The <paramref name="buildingVersion"/>'s prefix is also updated to "local/".
    /// <para>
    /// The building version tag and the version must be synchronized or an <see cref="ArgumentException"/> is thrown.
    /// <see cref="CKException"/> or a <see cref="LibGit2SharpException"/> may be thrown if anything goes wrong.
    /// </para>
    /// </summary>
    /// <returns>The updated tag in the repository and the version.</returns>
    public static (Tag, SVersion) CommitBuilding( Repo repo, Tag buildingVersionTag, SVersion buildingVersion )
    {
        Throw.CheckArgument( buildingVersion.IsBuilding() );
        Throw.CheckArgument( buildingVersionTag.CanonicalName.StartsWith( "refs/tags/building/v", StringComparison.Ordinal ) );
        Throw.CheckArgument( buildingVersionTag.CanonicalName.AsSpan( 19 ).EndsWith( buildingVersion.ToString(), StringComparison.Ordinal ) );

        // Defensive programming (allowOverwrite: true).
        var vTag = $"local/{buildingVersion}";
        var newTag = repo.GitRepository.Repository.Tags.Add( vTag,
                                                             buildingVersionTag.Target,
                                                             buildingVersionTag.Annotation.Tagger,
                                                             buildingVersionTag.Annotation.Message,
                                                             allowOverwrite: true );
        if( newTag == null )
        {
            var commit = (Commit)buildingVersionTag.Target;
            throw new CKException( $"Unable to apply tag '{vTag}' in '{repo.DisplayPath}' on commit '{commit.Sha.AsSpan( 0, 7 )} {commit.MessageShort}'." );
        }
        repo.GitRepository.Repository.Tags.Remove( buildingVersionTag );
        return (newTag, buildingVersion.SetParsedPrefix( "local/" ));
    }

    /// <summary>
    /// Gets the <see cref="BuildContentInfo"/>.
    /// </summary>
    /// <returns>The build content info.</returns>
    public override string ToString() => _buildContentInfo.ToString();

    /// <summary>
    /// Calls 'dotnet package list --format json --no-restore' and parses the result. The resulting
    /// packages are all the top level packages from all the projects for all the target frameworks
    /// (the same <see cref="PackageInstance.PackageId"/> may appear with different versions if
    /// conditional package references exist with restricted NuGet version ranges).
    /// <para>
    /// We could have captured the Requested (the version bound) in addition to the Resolved package version.
    /// This would allow a better impact computation by early filtering out useless package upgrades. This
    /// is doable but we almost never use NuGet version ranges. This would complexify the system for no real gain.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository to consider.</param>
    /// <param name="buildInfo">A build info description (used by log).</param>
    /// <param name="packages">The set of incoming packages or null on error.</param>
    /// <returns>True on success, false on error.</returns>
    /// <remarks>
    /// Collecting the package dependencies can be done in multiple ways:
    /// <list type="bullet">
    ///     <item>
    ///     By reading the obj/Configuration/ProjectName.csproj.nuget.dgspec.json file
    ///     that contains exactly what we need... But this is not really documented (we must iterate on
    ///     all the projects).
    ///     </item>
    ///     <item>
    ///     By reading the project.assets.json (that is documented) and extracts the top-level dependencies 
    ///     </item>
    ///     <item>
    ///     By using BuildAlyzer (see https://github.com/Buildalyzer/Buildalyzer, we must iterate on all the projects).
    ///     </item>
    ///     <item>
    ///     By calling 'dotnet package list --format json' and parsing the result. Contains exactly what we need
    ///     and can be called on the .sln (projects are handled).
    ///     </item>
    /// </list>
    /// =&gt; The simplest and most robust way is the 'dotnet package list --format json'.
    /// </remarks>
    public static bool GetConsumedPackages( IActivityMonitor monitor, Repo repo, string buildInfo, out ImmutableArray<PackageInstance> packages )
    {
        var stdOut = new StringBuilder();
        if( !repo.RunDotnet( monitor, "package list --format json --no-restore", stdOut ) )
        {
            packages = [];
            return false;
        }
        return ReadConsumedPackages( monitor, stdOut.ToString(), buildInfo, out packages );
    }

    /// <summary>
    /// Parses the result of a "dotnet package list" call.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="jsonPackageList">The json string to parse.</param>
    /// <param name="buildInfo">Any object: its ToString() method will be used for error and warning logs.</param>
    /// <param name="packages">The consumed package instances.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool ReadConsumedPackages( IActivityMonitor monitor,
                                             string jsonPackageList,
                                             object buildInfo,
                                             out ImmutableArray<PackageInstance> packages )
    {
        try
        {
            using var d = JsonDocument.Parse( jsonPackageList );
            if( !ReadProblems( monitor, buildInfo, d ) )
            {
                packages = [];
                return false;
            }
            packages = ReadPackages( d );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While reading Package list for '{buildInfo}'.", ex );
            packages = [];
            return false;
        }

        static bool ReadProblems( IActivityMonitor monitor, object buildInfo, JsonDocument d )
        {
            bool hasWarning = false;
            if( d.RootElement.TryGetProperty( "problems"u8, out var problems ) )
            {
                foreach( var p in problems.EnumerateArray() )
                {
                    if( p.GetProperty( "level"u8 ).GetString() == "error" )
                    {
                        monitor.Error( $"Package list for '{buildInfo}' has errors. See logs." );
                        return false;
                    }
                    else
                    {
                        hasWarning = true;
                    }
                }
            }
            if( hasWarning )
            {
                monitor.Warn( $"Package list for '{buildInfo}' has warnings. See logs." );
            }
            return true;
        }

        static ImmutableArray<PackageInstance> ReadPackages( JsonDocument d )
        {
            var result = new SortedSet<PackageInstance>();
            if( d.RootElement.TryGetProperty( "projects"u8, out var projects ) )
            {
                foreach( var p in projects.EnumerateArray() )
                {
                    if( p.TryGetProperty( "frameworks"u8, out var frameworks ) )
                    {
                        foreach( var f in frameworks.EnumerateArray() )
                        {
                            if( f.TryGetProperty( "topLevelPackages"u8, out var topLevelPackages ) )
                            {
                                foreach( var package in topLevelPackages.EnumerateArray() )
                                {
                                    string? packageId;
                                    if( !package.TryGetProperty( "id"u8, out var eId )
                                        || string.IsNullOrWhiteSpace( packageId = eId.GetString() ) )
                                    {
                                        Throw.InvalidDataException( $"Missing, null or empty 'topLevelPackages.id' property." );
                                    }
                                    else
                                    {
                                        result.Add( new PackageInstance( packageId,
                                                                         SVersion.Parse( package.GetProperty( "resolvedVersion"u8 ).GetString() ) ) );
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return result.ToImmutableArray();
        }

    }


}


