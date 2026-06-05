using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using CSemVer;
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Publish.Plugin;

sealed class WorldReleaseInfo
{
    readonly DateTime _buildDate;
    readonly ImmutableArray<RepoPublishInfo> _repos;
    readonly int _publishedLength;
    readonly bool _isCIBuild;
    string _title;

    /// <summary>
    /// Gets the build date of this release.
    /// </summary>
    public DateTime BuildDate => _buildDate;

    /// <summary>
    /// Gets the title.
    /// </summary>
    public string Title => _title;

    /// <summary>
    /// Gets whether this is a build on the "dev/" branch (produces CI packages).
    /// </summary>
    public bool IsCIBuild => _isCIBuild;

    /// <summary>
    /// Gets the published repositories information.
    /// <para>
    /// This is never empty.
    /// </para>
    /// </summary>
    public ImmutableArray<RepoPublishInfo> Repos => _repos;

    /// <summary>
    /// Gets the number of "items" to publish: the sum of the <see cref="Repos"/>'s <see cref="RepoPublishInfo.PublishedLength"/>
    /// plus one for this final set.
    /// </summary>
    public int PublishedLength => _publishedLength;

    WorldReleaseInfo( string title, DateTime buildDate, ImmutableArray<RepoPublishInfo> repos, int publishedLength, bool isCIBuild )
    {
        _title = title;
        _buildDate = buildDate;
        _repos = repos;
        _publishedLength = publishedLength;
        _isCIBuild = isCIBuild;
    }

    /// <summary>
    /// Creates from a <see cref="Roadmap"/>.
    /// </summary>
    /// <param name="buildDate">The build date to consider.</param>
    /// <param name="roadmap">The built roadmap.</param>
    /// <returns>A new world release.</returns>
    internal static WorldReleaseInfo Create( DateTime buildDate, Roadmap roadmap )
    {
        var repoInfos = new RepoPublishInfo[roadmap.SolutionPublishCount];
        int publishedLength = 0;
        int i = 0;
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( s.MustPublish )
            {
                var branchName = roadmap.IsCIBuild
                                    ? s.Solution.Branch.BranchName.DevName
                                    : s.Solution.Branch.BranchName.Name;

                var (version, content) = s.GetFinalPublishInfo();
                var r = new RepoPublishInfo( s.Repo, branchName, i, s.VersionInfo.BaseBuild.Version, version, content );
                repoInfos[i++] = r;
                publishedLength += r.PublishedLength;
            }
        }
        Throw.DebugAssert( i == repoInfos.Length );
        return new WorldReleaseInfo( buildDate.ToString( "yyyy.MM.dd+HH.mm" ),
                                     buildDate,
                                     ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ),
                                     publishedLength,
                                     roadmap.IsCIBuild );
    }

    /// <summary>
    /// Creates from a <see cref="FixWorkflow"/>.
    /// </summary>
    /// <param name="buildDate">The build date to consider.</param>
    /// <param name="fixWorkflow">The fix built.</param>
    /// <param name="results">The build results.</param>
    /// <returns>A new world release.</returns>
    internal static WorldReleaseInfo Create( DateTime buildDate, FixWorkflow fixWorkflow, ImmutableArray<BuildResult> results )
    {
        var repoInfos = new RepoPublishInfo[results.Length];
        int publishedLength = 0;
        for( int i = 0; i < results.Length; i++ )
        {
            var b = results[i];
            var r = new RepoPublishInfo( i, fixWorkflow.Targets[i].BranchName, SVersion.Create( b.Version.Major, b.Version.Minor, b.Version.Patch - 1), b );
            repoInfos[i++] = r;
            publishedLength += r.PublishedLength;
        }
        return new WorldReleaseInfo( fixWorkflow.ToString(), buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ), publishedLength, isCIBuild: false );
    }

}
