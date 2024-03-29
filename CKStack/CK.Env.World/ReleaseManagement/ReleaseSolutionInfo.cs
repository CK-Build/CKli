using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.Diff;
using CSemVer;
using SimpleGitVersion;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    class ReleaseSolutionInfo : IReleaseSolutionInfo
    {
        readonly GitRepository _repository;
        readonly ICommitInfo _commitVersionInfo;
        readonly int? _singleMajorConfig;
        readonly bool _onlyPatchConfig;

        ReleaseRoadmap _releaser;
        ReleaseInfo _previouslyResolvedInfo;
        ReleaseInfo _releaseInfo;

        internal ReleaseSolutionInfo( GitRepository repository,
                                      DependentSolution solution,
                                      (ICommitInfo Commit, int? SingleMajor, bool OnlyPatch) versionInfo,
                                      XElement? previous = null )
        {
            Debug.Assert( repository != null && solution != null );
            Solution = solution;
            _repository = repository;
            _commitVersionInfo = versionInfo.Commit;
            _singleMajorConfig = versionInfo.SingleMajor;
            _onlyPatchConfig = versionInfo.OnlyPatch;
            if( previous != null )
            {
                ReleaseNote = previous.Element( "ReleaseNote" ).Value;
                var releaseInfoXml = previous.Element( "ReleaseInfo" );
                if( releaseInfoXml != null )
                {
                    _releaseInfo = _previouslyResolvedInfo = new ReleaseInfo( releaseInfoXml );
                }
            }
        }

        internal void Initialize( ReleaseRoadmap r )
        {
            _releaser = r;
        }

        /// <summary>
        /// Gets the solution.
        /// </summary>
        public DependentSolution Solution { get; }

        /// <summary>
        /// Gets the current <see cref="ReleaseInfo"/>: the result of the last <see cref="EnsureReleaseInfo"/> call.
        /// May not be valid.
        /// </summary>
        public ReleaseInfo CurrentReleaseInfo => _releaseInfo;

        /// <summary>
        /// Gets the previous version, associated to a commit below the current one.
        /// This is null if no previous version has been found.
        /// </summary>
        public CSVersion? PreviousVersion => _commitVersionInfo.BestCommitBelow?.ThisTag;

        /// <summary>
        /// Gets or sets the release note.
        /// </summary>
        public string? ReleaseNote { get; set; }

        /// <summary>
        /// This is the heart of the global releaser system.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="versionSelector">The version selector.</param>
        /// <returns>A <see cref="ReleaseInfo"/> that can be invalid.</returns>
        public ReleaseInfo EnsureReleaseInfo( IActivityMonitor m, IReleaseVersionSelector versionSelector )
        {
            if( _releaseInfo.IsValid ) return _releaseInfo;
            var newR = ComputeReleaseInfo( m, versionSelector );
            if( newR.IsValid )
            {
                _previouslyResolvedInfo = _releaseInfo;
            }
            return _releaseInfo = newR;
        }

        /// <summary>
        /// Resets the current release info to an invalid one.
        /// </summary>
        public void ClearReleaseInfo()
        {
            if( _releaseInfo.IsValid )
            {
                _previouslyResolvedInfo = _releaseInfo;
            }
            _releaseInfo = new ReleaseInfo();
        }

        class PossibleVersions : IReadOnlyDictionary<ReleaseLevel, IReadOnlyList<CSVersion>>
        {
            static readonly ReleaseLevel[] _levels = new[] { ReleaseLevel.None, ReleaseLevel.Fix, ReleaseLevel.Feature, ReleaseLevel.BreakingChange };
            readonly IReadOnlyList<CSVersion>[] _versionList;

            public PossibleVersions( ReleaseInfo requirement, CSVersion? lastRelease, IReadOnlyList<CSVersion> possibles )
            {
                // For None level, we may choose to not release at all: use the last release.
                bool hasLastRelease = requirement.Level == ReleaseLevel.None && lastRelease != null;
                _versionList = new IReadOnlyList<CSVersion>[]
                {
                    hasLastRelease
                            ? new CSVersion[]{ lastRelease! }
                            : Array.Empty<CSVersion>(),
                    // For Fix level, possible versions are filtered by the original constraint.
                    requirement.Level <= ReleaseLevel.Fix
                            ? FilterVersions( possibles, requirement.Constraint ).ToArray()
                            : Array.Empty<CSVersion>(),
                    // For Feature level, possible versions must satisfy the HasFeatures constraint.
                    requirement.Level <= ReleaseLevel.Feature
                            ? FilterVersions( possibles, requirement.Constraint | ReleaseConstraint.HasFeatures ).ToArray()
                            : Array.Empty<CSVersion>(),
                    // For Feature level, possible versions must satisfy the HasBreakingChanges constraint.
                    FilterVersions( possibles, requirement.Constraint | ReleaseConstraint.HasBreakingChanges ).ToArray()
                };
                AllPossibleVersions = new HashSet<CSVersion>( _versionList.SelectMany( l => l ) );
            }

            public IReadOnlyCollection<CSVersion> AllPossibleVersions { get; }

            public IReadOnlyList<CSVersion> this[ReleaseLevel key] => _versionList[(int)key];

            public IEnumerable<ReleaseLevel> Keys => _levels;

            public IEnumerable<IReadOnlyList<CSVersion>> Values => _versionList;

            public int Count => 4;

            public bool ContainsKey( ReleaseLevel key ) => true;

            public IEnumerator<KeyValuePair<ReleaseLevel, IReadOnlyList<CSVersion>>> GetEnumerator()
            {
                return _versionList.Select( ( list, idx ) => new KeyValuePair<ReleaseLevel, IReadOnlyList<CSVersion>>( (ReleaseLevel)idx, list ) ).GetEnumerator();
            }

            public bool TryGetValue( ReleaseLevel key, out IReadOnlyList<CSVersion> value )
            {
                value = _versionList[(int)key];
                return true;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        class SelectorContext : IReleaseVersionSelectorContext
        {
            readonly ReleaseSolutionInfo _info;
            readonly PossibleVersions _possible;
            DiffResult? _diffResult;

            SelectorContext( ReleaseSolutionInfo info,
                             ITagCommit? lastRelease,
                             ReleaseInfo requirements,
                             ReleaseInfo actualRequirements,
                             PossibleVersions possibles,
                             IReadOnlyList<(ImportedLocalPackage, SVersion)>? publishedUpdates,
                             IReadOnlyList<(ImportedLocalPackage, SVersion)>? nonPublishedUpdates )
            {
                _info = info;
                PreviousVersion = lastRelease;
                Requirements = requirements;
                ActualRequirements = actualRequirements;
                _possible = possibles;
                CanUsePreviouslyResolvedInfo = info._previouslyResolvedInfo.IsValid
                                               && _possible.AllPossibleVersions.Contains( info._previouslyResolvedInfo.Version );
                PublishedUpdates = publishedUpdates ?? Array.Empty<(ImportedLocalPackage, SVersion)>();
                NonPublishedUpdates = nonPublishedUpdates ?? Array.Empty<(ImportedLocalPackage, SVersion)>();
            }

            public static SelectorContext Create( IActivityMonitor monitor,
                                                  ReleaseSolutionInfo info,
                                                  ReleaseInfo requirements,
                                                  IReadOnlyList<CSVersion> possibles,
                                                  IReadOnlyList<(ImportedLocalPackage, SVersion)>? publishedUpdates,
                                                  IReadOnlyList<(ImportedLocalPackage, SVersion)>? nonPublishedUpdates )
            {
                ReleaseInfo actualRequirements = requirements;
                if( info._singleMajorConfig != null )
                {
                    actualRequirements = actualRequirements.LowerForSingleMajor();
                    if( actualRequirements.Level != requirements.Level || actualRequirements.Constraint != requirements.Constraint )
                    {
                        monitor.Info( $"Repository configuration uses SingleMajor=\"{info._singleMajorConfig.Value}\". Using downgraded requirements from '{requirements}' to '{actualRequirements}' in order to select possible versions." );
                    }
                }
                // Normally, SingleMajor and OnlyPatch are exclusive but this is safer to handle both here.
                if( info._onlyPatchConfig )
                {
                    actualRequirements = actualRequirements.LowerForOnlyPatch();
                    if( actualRequirements.Level != requirements.Level || actualRequirements.Constraint != requirements.Constraint )
                    {
                        monitor.Info( $"Repository configuration uses OnlyPatch=\"true\". Using downgraded requirements from '{requirements}' to '{actualRequirements}' in order to select possible versions." );
                    }
                }
                // If Level is None - there's no special requirement on the version - the user can choose to not release
                // the version by using the last released one:
                //  - If a tag exists on the commit, this is the last one (it's necessarily valid, appears in the PossibleVersions and greater than
                //    the potentials BestCommitBelow and AlreadyExistingVersion.
                //  - Else we consider the best between BestCommitBelow and AlreadyExistingVersion.
                ITagCommit? lastRelease = info._commitVersionInfo.ThisReleaseTag
                                                ?? (info._commitVersionInfo.BestCommitBelow?.ThisTag > info._commitVersionInfo.AlreadyExistingVersion?.ThisTag
                                                    ? info._commitVersionInfo.BestCommitBelow
                                                    : info._commitVersionInfo.AlreadyExistingVersion);
                var possible = new PossibleVersions( actualRequirements, lastRelease?.ThisTag, possibles );

                return new SelectorContext( info, lastRelease, requirements, actualRequirements, possible, publishedUpdates, nonPublishedUpdates );
            }

            public DependentSolution Solution => _info.Solution;

            public ReleaseInfo PreviouslyResolvedInfo => _info._previouslyResolvedInfo;

            public bool CanUsePreviouslyResolvedInfo { get; }

            public IReadOnlyList<(ImportedLocalPackage, SVersion)> PublishedUpdates { get; }

            public IReadOnlyList<(ImportedLocalPackage, SVersion)> NonPublishedUpdates { get; }

            public IReadOnlyDictionary<ReleaseLevel, IReadOnlyList<CSVersion>> PossibleVersions => _possible;

            public IReadOnlyCollection<CSVersion> AllPossibleVersions => _possible.AllPossibleVersions;

            public ReleaseInfo Requirements { get; }

            public ReleaseInfo ActualRequirements { get; }

            public int? SingleMajorConfigured => _info._singleMajorConfig;

            public bool OnlyPatchConfigured => _info._onlyPatchConfig;

            public string CommitSha => _info._commitVersionInfo.FinalBuildInfo.CommitSha;

            public ITagCommit? PreviousVersion { get; }

            public string? ReleaseNote { get => _info.ReleaseNote; set => _info.ReleaseNote = value; }

            public DiffResult? GetProjectsDiff( IActivityMonitor m )
            {
                Throw.CheckState( PreviousVersion != null );
                if( _diffResult == null )
                {
                    var diffRoots = Solution.Solution.GeneratedArtifacts.Select( g => new DiffRoot( g.Artifact.TypedName, g.Project.ProjectSources ) );
                    _diffResult = _info._repository.GetDiff( m, PreviousVersion.CommitSha, diffRoots, withCommitMessages: true );
                }
                return _diffResult;
            }

            internal ReleaseLevel FinalLevel;
            internal CSVersion? FinalVersion;

            public bool IsCanceled { get; private set; }

            public bool HasChoice => FinalVersion != null;

            public bool IsAnswered => IsCanceled || HasChoice;

            public void Cancel()
            {
                Throw.CheckState( !IsAnswered );
                IsCanceled = true;
            }

            public void SetPreviouslyResolved()
            {
                Throw.CheckState( !IsAnswered );
                Throw.CheckState( CanUsePreviouslyResolvedInfo );
                FinalLevel = PreviouslyResolvedInfo.Level;
                FinalVersion = PreviouslyResolvedInfo.Version;
            }

            public void SetChoice( ReleaseLevel level, CSVersion version )
            {
                Throw.CheckState( !IsAnswered );
                Throw.CheckArgument( PossibleVersions[level].Contains( version ) );
                FinalLevel = level;
                FinalVersion = version;
            }
        }

        ReleaseInfo ComputeReleaseInfo( IActivityMonitor monitor, IReleaseVersionSelector versionSelector )
        {
            ReleaseInfo requirements = new ReleaseInfo();
            // Here we could have processed only the MinimalRequirements (so that the global process ordering
            // matches the dependency order).
            // But, we need to iterate though all Requirements in order to not miss a version
            // package upgrade.
            List<(ImportedLocalPackage, SVersion)>? nonPublishedUpdates = null;
            List<(ImportedLocalPackage, SVersion)>? publishedUpdates = null;
            foreach( var s in Solution.DirectRequirements )
            {
                var sInfo = _releaser.GetReleaseInfo( s.Index ).EnsureReleaseInfo( monitor, versionSelector );
                if( !sInfo.IsValid ) return new ReleaseInfo();
                bool isPublishedRequirement = Solution.PublishedRequirements.Contains( s );
                if( isPublishedRequirement )
                {
                    requirements = requirements.CombineRequirement( sInfo );
                }
                // Ignores build project dependencies here.
                // They will be upgraded by the ReleaseBuilder but don't participate in
                // impact propagation.
                foreach( var p in Solution.ImportedLocalPackages )
                {
                    if( p.Solution == s && p.Package.Version != sInfo.Version )
                    {
                        if( isPublishedRequirement )
                        {
                            if( publishedUpdates == null ) publishedUpdates = new List<(ImportedLocalPackage, SVersion)>();
                            publishedUpdates.Add( (p, sInfo.Version) );
                        }
                        else
                        {
                            if( nonPublishedUpdates == null ) nonPublishedUpdates = new List<(ImportedLocalPackage, SVersion)>();
                            nonPublishedUpdates.Add( (p, sInfo.Version) );
                        }
                    }
                }
            }

            Debug.Assert( _commitVersionInfo.NextPossibleVersions != null && _commitVersionInfo.PossibleVersions != null, "The commit info is valid." );

            // Handle package references updates.
            //  - if none and a Release tag already exists on the commit point, there
            //    is nothing to do (we exit directly with the only ReleaseLevel.None possible).
            //  - For all other cases, we compute the correct AllPossibleVersions and adjust the Release
            //    level of the requirements.
            IReadOnlyList<CSVersion> possibleVersions;
            if( publishedUpdates != null || nonPublishedUpdates != null )
            {
                // Since we'll need a commit to upgrade the package versions, we consider the NextPossibleVersions.
                possibleVersions = _commitVersionInfo.NextPossibleVersions;
            }
            else
            {
                // No need to upgrade this Solution to update its packages references.
                // If a tag exists on the commit point:
                //   1 - We must use it.
                //   2 - There is nothing more to do since it has already been published.
                // In both cases, we must keep the ReleaseLevel.None, however, the requirements may
                // not be None if we are processing an existing roadmap and updates have been
                // already applied to the files.
                if( _commitVersionInfo.ThisReleaseTag != null )
                {
                    monitor.Warn( $"This commit has already a version tag: {_commitVersionInfo.ThisReleaseTag.ThisTag}. We use it." );
                    if( !versionSelector.OnAlreadyReleased( monitor, Solution, _commitVersionInfo.ThisReleaseTag.ThisTag, false ) )
                    {
                        return new ReleaseInfo();
                    }
                    return new ReleaseInfo().WithVersion( _commitVersionInfo.ThisReleaseTag.ThisTag );
                }
                if( _commitVersionInfo.AlreadyExistingVersion != null )
                {
                    monitor.Info( $"This commit has a content version tag: {_commitVersionInfo.AlreadyExistingVersion.ThisTag}. We use it." );
                    if( !versionSelector.OnAlreadyReleased( monitor, Solution, _commitVersionInfo.AlreadyExistingVersion.ThisTag, true ) )
                    {
                        return new ReleaseInfo();
                    }
                    return new ReleaseInfo().WithVersion( _commitVersionInfo.AlreadyExistingVersion.ThisTag );
                }
                possibleVersions = _commitVersionInfo.PossibleVersions;
            }
            var ctx = SelectorContext.Create( monitor, this, requirements, possibleVersions, publishedUpdates, nonPublishedUpdates );
            versionSelector.ChooseFinalVersion( monitor, ctx );
            Debug.Assert( !ctx.HasChoice || ctx.FinalVersion != null, "HasChoice => FinalVersion != null" );
            return ctx.IsCanceled
                    ? new ReleaseInfo()
                    : (ctx.HasChoice
                        ? requirements.WithLevel( ctx.FinalLevel ).WithVersion( ctx.FinalVersion! )
                        : throw new InvalidOperationException( "At least Cancel or SetChoice must have been called." ));
        }

        /// <summary>
        /// Filters a set of versions according to a <see cref="ReleaseConstraint"/>.
        /// </summary>
        /// <param name="possibleVersions">Set of versions to filter.</param>
        /// <param name="c">The release constraint.</param>
        /// <returns>Filtered set of versions.</returns>
        public static IEnumerable<CSVersion> FilterVersions( IEnumerable<CSVersion> possibleVersions, ReleaseConstraint c )
        {
            IEnumerable<CSVersion> filtered = possibleVersions;

            // 1 - Filtering PreRelease if required.
            //     The 0 major is excluded from this filter.
            if( (c & ReleaseConstraint.MustBePreRelease) != 0 )
            {
                filtered = filtered.Where( v => v.Major == 0 || v.IsPrerelease );
            }
            // 2 If it has no feature, it can only be a patch.
            if( (c & ReleaseConstraint.HasFeatures) == 0 )
            {
                // When there is no breaking change nor feature, this is necessarily a Patch.
                return filtered.Where( v => v.IsPatch );
            }
            filtered = filtered.Where( v => !v.IsPatch );

            // 2 - If a breaking change or a feature occurred, this can not be a Patch, regardless
            //     of the Official vs. PreRelease status of the version.
            //     This filter is applied to the 0 major since the 0 major can perfectly handle this.
            if( (c & ReleaseConstraint.HasBreakingChanges) != ReleaseConstraint.HasFeatures )
            {
                return filtered.Where( p => p.Minor == 0 || p.Major == 0 );
            }
            return filtered.Where( p => p.Minor != 0 || p.Major == 0 );
        }

        internal XElement ToXml()
        {
            return new XElement( XmlNames.xS,
                        new XAttribute( XmlNames.xName, Solution.Solution.Name ),
                        new XAttribute( XmlNames.xSubPath, Solution.Solution.FullPath ),
                        new XAttribute( XmlNames.xCommitSha, _commitVersionInfo.FinalBuildInfo.CommitSha ),
                        _releaseInfo.ToXml(),
                        new XElement( XmlNames.xReleaseNote, new XCData( ReleaseNote ?? String.Empty ) ) );
        }
    }
}
