using CK.Core;
using CSemVer;
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
        readonly CommitVersionInfo _commitVersionInfo;
        ReleaseRoadmap _releaser;
        ReleaseInfo _previouslyResolvedInfo;
        ReleaseInfo _releaseInfo;

        internal ReleaseSolutionInfo(
            IDependentSolution solution,
            CommitVersionInfo versionInfo,
            XElement previous = null )
        {
            Debug.Assert( solution != null && versionInfo != null );
            Solution = solution;
            _commitVersionInfo = versionInfo;
            if( previous != null )
            {
                ReleaseNote = previous.Element( "ReleaseNote" ).Value;
                var releaseInfoXml = previous.Element( "ReleaseInfo" );
                if( releaseInfoXml != null ) _previouslyResolvedInfo = new ReleaseInfo( releaseInfoXml );
                bool isSame = (string)previous.Attribute( "CommitSha" ) == versionInfo.CommitSha;
                if( isSame )
                {
                    _releaseInfo = _previouslyResolvedInfo;
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
        public IDependentSolution Solution { get; }

        /// <summary>
        /// Gets the current <see cref="ReleaseInfo"/>: the result of the last <see cref="EnsureReleaseInfo"/> call.
        /// May not be valid.
        /// </summary>
        public ReleaseInfo CurrentReleaseInfo => _releaseInfo;

        /// <summary>
        /// Gets the previous version, associated to a commit below the current one.
        /// This is null if no previous version has been found.
        /// </summary>
        public CSVersion PreviousVersion => _commitVersionInfo.PreviousVersion;

        /// <summary>
        /// Gets or sets the release note.
        /// </summary>
        public string ReleaseNote { get; set; }

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

            IReadOnlyList<CSVersion>[] _versionList;

            public PossibleVersions( ReleaseInfo requirement, CSVersion lastRelease, IReadOnlyList<CSVersion> possibles )
            {
                // For None level, we may choose to not release at all: use the last release.
                bool hasLastRelease = requirement.Level == ReleaseLevel.None && lastRelease != null;
                _versionList = new IReadOnlyList<CSVersion>[]
                {
                    hasLastRelease
                            ? new CSVersion[]{ lastRelease }
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

            public SelectorContext(
                    ReleaseSolutionInfo info,
                    ReleaseInfo requirements,
                    IReadOnlyList<CSVersion> possibles )
            {
                _info = info;
                Requirements = requirements;
                _possible = new PossibleVersions( requirements, info._commitVersionInfo.PreviousVersion, possibles );
                CanUsePreviouslyResolvedInfo = info._previouslyResolvedInfo.IsValid
                                               && info._previouslyResolvedInfo.IsCompatibleWith( requirements.Level, requirements.Constraint )
                                               && _possible.AllPossibleVersions.Contains( info._previouslyResolvedInfo.Version );
            }

            public IDependentSolution Solution => _info.Solution;

            public ReleaseInfo PreviouslyResolvedInfo => _info._previouslyResolvedInfo;

            public bool CanUsePreviouslyResolvedInfo { get; }

            public IReadOnlyDictionary<ReleaseLevel, IReadOnlyList<CSVersion>> PossibleVersions => _possible;

            public IReadOnlyCollection<CSVersion> AllPossibleVersions => _possible.AllPossibleVersions;

            public ReleaseInfo Requirements { get; }

            public CSVersion PreviousVersion => _info._commitVersionInfo.PreviousVersion;

            public string PreviousVersionCommitSha => _info._commitVersionInfo.PreviousVersionCommitSha;

            public string ReleaseNote { get => _info.ReleaseNote; set => _info.ReleaseNote = value; }

            internal ReleaseLevel FinalLevel;
            internal CSVersion FinalVersion;

            public bool IsCanceled { get; private set; }

            public bool HasChoice => FinalVersion != null;

            public bool IsAnswered => IsCanceled || HasChoice;

            public void Cancel()
            {
                if( HasChoice ) throw new InvalidOperationException( nameof( HasChoice ) );
                if( IsCanceled ) throw new InvalidOperationException( nameof( IsCanceled ) );
                IsCanceled = true;
            }

            public void SetChoice( ReleaseLevel level, CSVersion version )
            {
                if( HasChoice ) throw new InvalidOperationException( nameof( HasChoice ) );
                if( IsCanceled ) throw new InvalidOperationException( nameof( IsCanceled ) );
                if( !PossibleVersions[level].Contains( version ) ) throw new ArgumentException( "Not a version for level.", nameof(version) );
                FinalLevel = level;
                FinalVersion = version;
            }
        }

        ReleaseInfo ComputeReleaseInfo( IActivityMonitor m, IReleaseVersionSelector versionSelector )
        {
            ReleaseInfo requirements = new ReleaseInfo();
            // Here we could have processed only the MinimalRequirements (so that the global process ordering
            // matches the dependency order).
            // But, we need to iterate though all Requirements in order to not miss a version
            // package upgrade.
            bool hasUpdates = false;
            foreach( var s in Solution.Requirements )
            {
                var sInfo = _releaser.GetReleaseInfo( s.Index ).EnsureReleaseInfo( m, versionSelector );
                if( !sInfo.IsValid ) return new ReleaseInfo();
                if( Solution.PublishedRequirements.Contains( s ) )
                {
                    requirements = requirements.CombineRequirement( sInfo );
                }
                // Ignores build project dependencies here.
                // They will be upgraded by the ReleaseBuilder but don't participate in
                // impact propagation.
                hasUpdates |= Solution.ImportedLocalPackages
                                      .Any( p => p.Solution == s && p.Package.Version != sInfo.Version );
            }

            // Handle package references updates.
            //  - if none and a Release tag already exists on the commit point, there
            //    is nothing to do (we exit directly with the only ReleaseLevel.None possible).
            //  - For all other cases, we compute the correct AllPossibleVersions and adjust the Release
            //    level of the requirements.
            IReadOnlyList<CSVersion> possibleVersions;
            if( hasUpdates )
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
                if( _commitVersionInfo.ReleaseVersion != null )
                {
                    m.Warn( $"This commit has already a version tag: {_commitVersionInfo.ReleaseVersion}." );
                    if( !versionSelector.OnAlreadyReleased( m, Solution, _commitVersionInfo.ReleaseVersion, false ) )
                    {
                        return new ReleaseInfo();
                    }
                    return new ReleaseInfo().WithVersion( _commitVersionInfo.ReleaseVersion );
                }
                if( _commitVersionInfo.ReleaseContentVersion != null )
                {
                    // TODO: ensure that this is the tag of the commit point merged into master branch.
                    m.Info( $"This commit has a content version tag: {_commitVersionInfo.ReleaseContentVersion}. We use it." );
                    if( !versionSelector.OnAlreadyReleased( m, Solution, _commitVersionInfo.ReleaseContentVersion, true ) )
                    {
                        return new ReleaseInfo();
                    }
                    return new ReleaseInfo().WithVersion( _commitVersionInfo.ReleaseContentVersion );
                }
                possibleVersions = _commitVersionInfo.PossibleVersions;
            }
            var ctx = new SelectorContext( this, requirements, possibleVersions );
            versionSelector.ChooseFinalVersion( m, ctx );
            return ctx.IsCanceled
                    ? new ReleaseInfo()
                    : (ctx.HasChoice
                        ? requirements.WithLevel( ctx.FinalLevel ).WithVersion( ctx.FinalVersion )
                        : throw new InvalidOperationException( "At least Canel or SetChoice must have been called." ));
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
            // 2 - If a breaking change or a feature occurred, this can not be a Patch, regardless
            //     of the Official vs. PreRelease status of the version.
            //     This filter is applied to the 0 major since the 0 major can perfectly handle this.
            if( (c & (ReleaseConstraint.HasBreakingChanges | ReleaseConstraint.HasFeatures)) != 0 )
            {
                filtered = filtered.Where( v => !v.IsPatch );
            }
            else 
            {
                // When there is no breaking change nor feature, this is necessarily a Patch.
                filtered = filtered.Where( v => v.IsPatch );
            }

            // 3 - On a breaking change, Official version must have their Major bumped (ie. their Minor and Patch must be 0).
            //     The 0 major is excluded from this filter.
            if( (c & ReleaseConstraint.HasBreakingChanges) == ReleaseConstraint.HasBreakingChanges )
            {
                filtered = filtered.Where( v => v.Major == 0 || v.IsPrerelease || (v.Minor == 0 && v.Patch == 0) );
            }
            return filtered;
        }

        internal XElement ToXml()
        {
            return new XElement( "S",
                        new XAttribute( "Name", Solution.UniqueSolutionName ),
                        new XAttribute( "SubPath", Solution.GitRepository.SubPath ),
                        new XAttribute( "CommitSha", _commitVersionInfo.CommitSha ),
                        _releaseInfo.ToXml(),
                        new XElement( "ReleaseNote", new XCData( ReleaseNote ?? String.Empty ) ));
        }
    }
}
