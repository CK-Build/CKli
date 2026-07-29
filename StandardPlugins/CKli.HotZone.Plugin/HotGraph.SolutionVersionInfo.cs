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
        /// Gets the <see cref="TagCommitTree"/> for this <see cref="GitSolution.GitBranch"/>.
        /// </summary>
        public TagCommitTree TagCommitTree => _tagCommitTree;

        /// <summary>
        /// Gets the base commit that is the <see cref="VersionTagInfo.HotZoneInfo.LastPublishedStable"/>.
        /// Can be "+fake" or "+deprecated".
        /// </summary>
        public TagCommit BaseBuild => _tagCommitTree.LastStable;

        /// <summary>
        /// Gets the last built version to consider in the regular <see cref="Solution.Branch"/> or its "dev/" branch.
        /// </summary>
        /// <param name="ciBuild">Whether we are in a CI build context.</param>
        /// <returns>The CI or non CI last build.</returns>
        public BuiltVersion GetLastBuild( bool ciBuild )
        {
           return new BuiltVersion( this, _tagCommitTree.GetBestBuildFor( _solution.Branch.BranchName, ciBuild ).Commit );
        }


    }
}
