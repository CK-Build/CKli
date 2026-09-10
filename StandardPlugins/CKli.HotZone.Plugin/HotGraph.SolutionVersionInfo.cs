using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;

namespace CKli.HotZone.Plugin;

public sealed partial class HotGraph
{
    /// <summary>
    /// Captures version related information for a <see cref="HotGraph.Solution"/>.
    /// Exposed by <see cref="Solution.VersionInfo"/> but initialized by a successful call to <see cref="HotGraph.GetPackageUpdater(IActivityMonitor, VersionTagPlugin)"/>.
    /// Requires that <see cref="VersionTagPlugin"/> has no issue.
    /// <para>
    /// This tracks branches that must be built 
    /// </para>
    /// </summary>
    public sealed class SolutionVersionInfo
    {
        readonly Solution _solution;
        readonly VersionTagInfo _info;
        readonly string _builtTipSha;
        readonly TagCommitTree _tagCommitTree;

        internal SolutionVersionInfo( Solution solution,
                                      VersionTagInfo info,
                                      string builtTipSha,
                                      TagCommitTree tagCommitTree )
        {
            Throw.DebugAssert( info.HotZone != null && info.HotZone.HotZoneIssue == null );
            _solution = solution;
            _info = info;
            _builtTipSha = builtTipSha;
            _tagCommitTree = tagCommitTree;
        }


        // The commit the solution has been read from, not the current branch tip: this is dirty when the
        // solution itself has been replaced (SetDevSolution reads another commit), not when the branch moved
        // under a solution nobody re-read. A solution bound to a commit (the graph branch is missing in the
        // repository and this is the commit it would be created at) is never dirty by design.
        internal bool IsDirty => _builtTipSha != _solution.GitSolution.Commit.Sha;

        /// <summary>
        /// Captures the last <see cref="Version"/>, its <see cref="TagCommit"/> and <see cref="BranchName"/>, to consider in a build
        /// context (considering the <see cref="Solution.Branch"/> and whether we are building regular or CI build).
        /// <para>
        /// Created by <see cref="SolutionVersionInfo.GetLastBuild(bool)"/>.
        /// </para>
        /// </summary>
        public readonly struct LastBuiltVersion
        {
            readonly SolutionVersionInfo _info;
            readonly TagCommit _tagCommit;
            readonly BranchName _branchName;
            readonly bool _isCI0;

            internal LastBuiltVersion( SolutionVersionInfo info, in (TagCommit Commit, SVersion Version, BranchName Branch) bestBuild )
            {
                _info = info;
                _tagCommit = bestBuild.Commit;
                _branchName = bestBuild.Branch;
                Throw.DebugAssert( ReferenceEquals( bestBuild.Version, _tagCommit.Version ) || ReferenceEquals( bestBuild.Version, _tagCommit.CI0Version ) );
                _isCI0 = bestBuild.Version == _tagCommit.CI0Version;
            }

            /// <summary>
            /// Gets the tagged commit.
            /// </summary>
            public TagCommit TagCommit => _tagCommit;

            /// <summary>
            /// Gets the built version (either <see cref="TagCommit.Version"/> or <see cref="TagCommit.CI0Version"/>).
            /// </summary>
            public SVersion Version => _isCI0 ? _tagCommit.CI0Version! : _tagCommit.Version;

            /// <summary>
            /// Gets the Git tag (either <see cref="TagCommit.Tag"/> or <see cref="TagCommit.CI0VersionTag"/>).
            /// </summary>
            public Tag Tag => _isCI0 ? _tagCommit.CI0VersionTag! : _tagCommit.Tag;

            /// <summary>
            /// Gets the closest branch name to <see cref="Solution.Branch"/> from which this <see cref="TagCommit"/> is available.
            /// </summary>
            public BranchName BranchName => _branchName;

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
            public bool VersionMustBuild => Version.BuildMetaData.Length > 0;

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
        /// Gets the repository HotZone information.
        /// </summary>
        public VersionTagInfo.HotZoneInfo HotZone => _info.HotZone!;

        /// <summary>
        /// Gets the version info of the <see cref="Repo"/>.
        /// </summary>
        public VersionTagInfo VersionTagInfo => _info;

        /// <summary>
        /// Gets the <see cref="TagCommitTree"/> for this <see cref="GitSolution.GitBranch"/>.
        /// </summary>
        public TagCommitTree TagCommitTree => _tagCommitTree;

        /// <summary>
        /// Gets the base commit that is the <see cref="VersionTagInfo.HotZoneInfo.LastStable"/>.
        /// Can be "+fake" or "+deprecated".
        /// </summary>
        public TagCommit BaseBuild => _tagCommitTree.LastStable;

        /// <summary>
        /// Gets the last built version to consider in the regular <see cref="Solution.Branch"/> or its "dev/" branch.
        /// </summary>
        /// <param name="ciBuild">Whether we are in a CI build context.</param>
        /// <returns>The CI or non CI last build.</returns>
        public LastBuiltVersion GetLastBuild( bool ciBuild )
        {
           return new LastBuiltVersion( this, _tagCommitTree.GetBestBuildFor( _solution.Branch.BranchName, ciBuild ) );
        }


    }
}
