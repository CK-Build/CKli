using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System.Collections.Generic;
using System.Linq;

namespace CKli.HotZone.Plugin;

public sealed partial class HotGraph
{
    /// <summary>
    /// Captures version related information for a <see cref="Solution"/>.
    /// Exposed by <see cref="Solution.VersionInfo"/> but initialized by a successful call to <see cref="HotGraph.GetPackageUpdater(IActivityMonitor)"/>.
    /// Requires that <see cref="VersionTagPlugin"/> has no issue.
    /// <para>
    /// This tracks branches that must be built 
    /// </para>
    /// </summary>
    public sealed class SolutionVersionInfo
    {
        readonly Solution _solution;
        readonly VersionTagInfo _info;
        readonly HashSet<HotBranch> _requiredBuildCollector;
        readonly string _builtTipSha;
        readonly IReadOnlyList<Commit> _commitsFromBaseBuild;
        readonly IReadOnlyList<(TagCommit T, int Level)> _tagCommitTree;
        TagCommit? _lastBuildInCI;
        TagCommit? _lastBuildInNonCI;

        internal SolutionVersionInfo( Solution solution,
                                      VersionTagInfo info,
                                      HashSet<HotBranch> requiredBuildCollector,
                                      string builtTipSha,
                                      List<Commit> commitsFromBaseBuild,
                                      IReadOnlyList<(TagCommit T, int Level)> tagCommitTree )
        {
            Throw.DebugAssert( info.HotZone != null && info.HotZone.HotZoneIssue == null );
            _solution = solution;
            _info = info;
            _requiredBuildCollector = requiredBuildCollector;
            _builtTipSha = builtTipSha;
            _commitsFromBaseBuild = commitsFromBaseBuild;
            _tagCommitTree = tagCommitTree;
        }


        internal bool IsDirty => _builtTipSha != _solution.GitSolution.GitBranch.Tip.Sha;

        /// <summary>
        /// Captures the last <see cref="TagCommit"/> to consider in a build context (branch and whether we are building
        /// regular or CI build).
        /// </summary>
        public readonly struct BuiltVersion
        {
            readonly SolutionVersionInfo _info;
            readonly TagCommit _tagCommit;

            internal BuiltVersion( SolutionVersionInfo info, TagCommit tagCommit )
            {
                _info = info;
                _tagCommit = tagCommit;
            }

            /// <summary>
            /// Gets the version tag.
            /// </summary>
            public TagCommit TagCommit => _tagCommit;

            /// <summary>
            /// Gets whether a build is required because <see cref="TagCommit"/> version is either
            /// a "+fake" or a "+deprecated" version and should not be used to reference any package
            /// produced by this solution.
            /// </summary>
            /// <remarks>
            /// We explored the approach in which "+deprecated" on the last build tag necessarily triggers a build but a "+fake"
            /// is "skippable" (ie. depends on the selected pivots). We rejected this because such skipped solution may need their
            /// version to be updated in downstream repositories and the version to "last good version" to apply doesn't exist
            /// when the fake version appears alone (no other version tags exist or, for any reasons, the other version tags have
            /// been deprecated or invalidated).
            /// <para>
            /// This MAY be handled one day but this has been considered too fragile: a "+fake" version is currently not skippable.
            /// </para>
            /// </remarks>
            public bool VersionMustBuild => !_tagCommit.IsRegularVersion;

            /// <summary>
            /// Gets whether a build is required because <see cref="TagCommit"/>'s content is not the same as
            /// the dev's git branch's tip content if it exists (otherwise, the <see cref="HotBranch.GitBranch"/> is used).
            /// </summary>
            public bool HasCodeChange
            {
                get
                {
                    HotBranch hotBranch = _info._solution.Branch;
                    Throw.DebugAssert( hotBranch.Exists );
                    return _tagCommit.Commit.Tree.Sha != (hotBranch.GitDevBranch ?? hotBranch.GitBranch).Tip.Tree.Sha;
                }
            }

            /// <summary>
            /// Overridden to return the <see cref="TagCommit"/>.
            /// </summary>
            /// <returns>The tag commit.</returns>
            public override string ToString() => _tagCommit.ToString();
        }

        /// <summary>
        /// Gets the repository.
        /// </summary>
        public Repo Repo => _info.Repo;

        /// <summary>
        /// Gets the HotGraph solution.
        /// </summary>
        public Solution Solution => _solution;

        /// <inheritdoc cref="Solution.GitSolution" />
        public GitSolution GitSolution => _solution.GitSolution;

        /// <summary>
        /// Gets the version info of the <see cref="Repo"/>.
        /// </summary>
        public VersionTagInfo VersionTagInfo => _info;

        /// <summary>
        /// Gets the base commit that is the <see cref="VersionTagInfo.HotZoneInfo.LastStable"/>.
        /// Can be "+fake" or "+deprecated".
        /// </summary>
        public TagCommit BaseBuild => _info.HotZone!.LastStable;

        /// <summary>
        /// Gets the last built version to consider in the <see cref="Solution.Branch"/> and CI build context.
        /// </summary>
        public BuiltVersion LastBuildInCI
        {
            get
            {
                if( _lastBuildInCI == null )
                {
                    // Note: TagCommit.CompareTo reverts the SVersion.CompareTo order.
                    //       Using Min() here gives us the greatest version.
                    _lastBuildInCI = _tagCommitsFromBaseBuild.Min() ?? BaseBuild;
                }
                return new BuiltVersion( this, _lastBuildInCI );
            }
        }

        /// <summary>
        /// Gets the last built version to consider in the regular <see cref="Solution.Branch"/>.
        /// </summary>
        public BuiltVersion LastBuildInNonCI
        {
            get
            {
                if( _lastBuildInNonCI == null )
                {
                    Throw.CheckState( "Currently, only 'stable' branch is supported.", _solution.Branch.BranchName.Index == 0 );
                    // In the "stable" branch, the last commit is by design the BaseBuild.
                    _lastBuildInNonCI = BaseBuild;
                }
                return new BuiltVersion( this, _lastBuildInNonCI );
            }
        }

        /// <summary>
        /// Gets the last built version to consider in the regular <see cref="Solution.Branch"/> or its "dev/" branch.
        /// </summary>
        /// <param name="ciBuild">Whether we are in a CI build context.</param>
        /// <returns>The CI or non CI last build.</returns>
        public BuiltVersion GetLastBuild( bool ciBuild )
        {
            if( ciBuild )
            {
                if( _lastBuildInCI == null )
                {
                    Throw.DebugAssert( !_info.HasIssue );
                    if( !_info.HotZone.TryGetLastBuild( _solution.Branch, ciBuild, out var buildRequired, out _lastBuildInCI ) )
                    {
                        _requiredBuildCollector.Add( _solution.Branch );
                        _lastBuildInCI = _info.HotZone.LastStable;
                    }
                }
                return new BuiltVersion( this, _lastBuildInCI );
            }
            return ciBuild ? LastBuildInCI : LastBuildInNonCI;
        }

        /// <summary>
        /// Gets all the commits from <see cref="GitSolution"/>'s git branch's tip down to <see cref="BaseBuild"/>.
        /// </summary>
        public IReadOnlyList<Commit> CommitsFromBaseBuild => _commitsFromBaseBuild;

        /// <summary>
        /// Gets the <see cref="CommitsFromBaseBuild"/> joined with <see cref="VersionTagInfo.TagCommitsBySha"/>.
        /// </summary>
        public IReadOnlyList<TagCommit> TagCommitsFromBaseBuild => _tagCommitsFromBaseBuild;

    }
}
