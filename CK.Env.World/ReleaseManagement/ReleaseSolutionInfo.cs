using CK.Core;
using CK.Env;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    public class ReleaseSolutionInfo
    {
        readonly List<UpdatePackageInfo> _updatePackageInfos;
        readonly CommitVersionInfo _commitVersionInfo;
        ReleaseRoadmap _releaser;
        ReleaseInfo _previousReleaseInfo;
        ReleaseInfo _releaseInfo;

        internal ReleaseSolutionInfo( IDependentSolution solution, CommitVersionInfo versionInfo, XElement previous = null )
        {
            Debug.Assert( solution != null && versionInfo != null );
            Solution = solution;
            _commitVersionInfo = versionInfo;
            _updatePackageInfos = new List<UpdatePackageInfo>();
            if( previous != null )
            {
                ReleaseNote = previous.Element( "ReleaseNote" ).Value;
                var releaseInfoXml = previous.Element( "ReleaseInfo" );
                if( releaseInfoXml != null ) _previousReleaseInfo = new ReleaseInfo( releaseInfoXml );
                 
                bool isSame = (string)previous.Attribute( "CommitSha" ) == versionInfo.CommitSha;
                if( isSame )
                {
                    _releaseInfo = _previousReleaseInfo;
                    var updatesXml = previous.Element( "UpdatePackageInfos" );
                    if( updatesXml != null )
                    {
                        _updatePackageInfos.AddRange( updatesXml.Elements().Select( u => new UpdatePackageInfo( u, solution.UniqueSolutionName ) ) );
                    }
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
        /// Gets the previous <see cref="ReleaseInfo"/>. May not be valid.
        /// </summary>
        public ReleaseInfo PreviousReleaseInfo => _previousReleaseInfo;

        /// <summary>
        /// Gets or sets the release note.
        /// </summary>
        public string ReleaseNote { get; set; }

        /// <summary>
        /// Gets all possible versions. 
        /// </summary>
        public IReadOnlyList<CSVersion> AllPossibleVersions { get; private set; }

        /// <summary>
        /// Gets the  required packages updates.
        /// </summary>
        public IReadOnlyList<UpdatePackageInfo> UpdatePackageInfos => _updatePackageInfos;

        /// <summary>
        /// Gets the assembly build info to use.
        /// Null when <see cref="ReleaseInfo.IsValid"/> is false.
        /// </summary>
        public ICommitAssemblyBuildInfo AssemblyBuildInfo { get; private set; }


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
                _previousReleaseInfo = _releaseInfo;
                AssemblyBuildInfo = _commitVersionInfo.AssemblyBuildInfo.WithReleaseVersion( newR.Version );
            }
            return _releaseInfo = newR;
        }

        ReleaseInfo ComputeReleaseInfo( IActivityMonitor m, IReleaseVersionSelector versionSelector )
        {
            AssemblyBuildInfo = null;
            _updatePackageInfos.Clear();
            ReleaseInfo requirements = new ReleaseInfo();
            // Here we could have processed only the MinimalRequirements (so that the global process ordering
            // matches the dependency order).
            // But, we need to iterate though all Requirements in order to not miss a version
            // package upgrade.
            foreach( var s in Solution.Requirements )
            {
                var sInfo = _releaser.FindBySolution( s ).EnsureReleaseInfo( m, versionSelector );
                if( !sInfo.IsValid ) return new ReleaseInfo();
                if( Solution.PublishedRequirements.Contains( s ) )
                {
                    requirements = requirements.CombineRequirement( sInfo );
                }
                var updates = Solution.ImportedLocalPackages
                                      .Where( p => p.Solution == s && p.Package.Version != sInfo.Version )
                                      .Select( p => new UpdatePackageInfo(
                                                         solutionName: p.SecondarySolutionName ?? p.Solution.UniqueSolutionName,
                                                         projectName: p.ProjectName,
                                                         package: new VersionedPackage( p.Package.PackageId, sInfo.Version ) ) );
                _updatePackageInfos.AddRange( updates );
            }
            // Handle package references updates.
            //  - if none and a Release tag already exists on the commit point, there
            //    is nothing to do (we exit directly with the only ReleaseLevel.None possible).
            //  - For all other cases, we compute the correct AllPossibleVersions and adjust the Release
            //    level of the requirements.
            if( _updatePackageInfos.Count > 0 )
            {
                // Since we'll need a commit to upgrade the package versions, we consider the NextPossibleVersions.
                AllPossibleVersions = _commitVersionInfo.NextPossibleVersions;
            }
            else
            {
                // No need to upgrade this Solution to update its packages references.
                if( requirements.Level != ReleaseLevel.None )
                {
                    // This is odd... A dependency is a fix (or more). Its own version must have been bumped
                    // and so there should have been an impact on our package versions.
                    // (Otherwise this would mean that the solution dependency is wrong!)
                    throw new Exception( $"{Solution.UniqueSolutionName}: A dependent solution implied a ReleaseLevel of {requirements.Level} but none of our packages had to be updated." );
                }
                Debug.Assert( requirements.Level == ReleaseLevel.None );
                // If a tag exists on the commit point:
                //   1 - We must use it.
                //   2 - There is nothing more to do since it has already been published.
                // We must exit here directly since the code below does not cover this edge case:
                // when there is only one possible version, the code below will handle/ask release level
                // and here we must keep the ReleaseLevel.None.
                if( _commitVersionInfo.ReleaseVersion != null )
                {
                    m.Info( $"This commit has already a version tag: {_commitVersionInfo.ReleaseVersion}. We use it." );
                    AllPossibleVersions = new[] { _commitVersionInfo.ReleaseVersion };
                    return requirements.WithVersion( _commitVersionInfo.ReleaseVersion );
                }
                var vContent = _commitVersionInfo.ReleaseContentVersion;
                if( vContent != null )
                {
                    // TODO: ensure that this is the tag of the commit point merged into master branch.
                    m.Info( $"This commit has a content version tag: {vContent}. We use it." );
                    AllPossibleVersions = new[] { vContent };
                    return requirements.WithVersion( vContent );
                }
                AllPossibleVersions = _commitVersionInfo.PossibleVersions;
            }

            if( requirements.Level == ReleaseLevel.None )
            {
                // There are no reasons to release because of the dependencies.
                // The new code that is contained in this commit:
                // - MAY have no actual changes: the previous version could be used.
                // - or MUST be released at least as a fix.
                var prevVersion = _commitVersionInfo.PreviousVersion;
                if( prevVersion != null )
                {
                    bool? usePrevious = versionSelector.CanUsePreviousVersion( m, Solution, prevVersion );
                    if( !usePrevious.HasValue ) return new ReleaseInfo();
                    if( usePrevious.Value == true )
                    {
                        AllPossibleVersions = new[] { prevVersion };
                        return requirements.WithVersion( prevVersion );
                    }
                }
                requirements = requirements.WithLevel( ReleaseLevel.Fix );
            }

            Debug.Assert( requirements.Level >= ReleaseLevel.Fix );

            // Handles the versions now.
            if( AllPossibleVersions.Count == 0 )
            {
                m.Error( "No PossibleVersions exist for this commit point according to SimpleGitVersion." );
                return new ReleaseInfo();
            }

            var filteredVersions = FilterVersions( AllPossibleVersions, requirements.Constraint, false ).ToList();
            if( filteredVersions.Count == 0 )
            {
                m.Error( $"No PossibleVersions exist for this commit point with Constraint = {requirements.Constraint}." );
                return new ReleaseInfo();
            }

            if( filteredVersions.Count == 1 )
            {
                return HandleSingleVersion( m, versionSelector, requirements, filteredVersions[0] );
            }
            // Decide the release level.
            if( requirements.Level != ReleaseLevel.BreakingChange )
            {
                var level = versionSelector.ChooseReleaseLevel( m, Solution, requirements.Level );
                if( level == ReleaseLevel.None ) return _releaseInfo;
                if( level < requirements.Level ) throw new InvalidOperationException( "ChooseReleaseLevel can not decrease the current level." );
                requirements = requirements.WithLevel( level );
                filteredVersions = FilterVersions( filteredVersions, requirements.Constraint, true ).ToList();
                if( filteredVersions.Count == 1 )
                {
                    return HandleSingleVersion( m, versionSelector, requirements, filteredVersions[0] );
                }
            }
            // Multiple versions are possible.
            Debug.Assert( filteredVersions.Count > 1, "There are still multiple versions." );
            var finalVersion = versionSelector.ChooseFinalVersion( m, Solution, filteredVersions, requirements );
            if( finalVersion == null ) return _releaseInfo;
            if( !filteredVersions.Contains( finalVersion ) ) throw new InvalidOperationException( "ChooseFinalVersion must return one of the possible versions." );
            return requirements.WithVersion( finalVersion );
        }

        ReleaseInfo HandleSingleVersion( IActivityMonitor m, IReleaseVersionSelector versionSelector, ReleaseInfo requirements, CSVersion v )
        {
            if( requirements.Level == ReleaseLevel.BreakingChange )
            {
                // The version is a breaking change and the requirements.Level is actually
                // a breaking change, this is fine.
                m.Info( $"Version automatically inferred: {v}." );
                return requirements.WithVersion( v );
            }
            // There is only one version and we are not on a breaking change propagation.
            // The risk here is to release a version that does not indicate the correct type of change to dependent
            // solutions.
            // If the version is a pre release then all dependent versions will also be prereleases, 
            // pre-releases don't care about breaking changes however they care about
            // fixes vs. features or breaking changes for automatic computation.
            if( v.IsPrerelease || v.Major == 0 )
            {
                ReleaseLevel level;
                if( requirements.Level == ReleaseLevel.Fix )
                {
                    level = versionSelector.GetPreReleaseSingleVersionFixActualLevel( m, Solution, v, v.Major == 0 );
                    if( level == ReleaseLevel.None ) return new ReleaseInfo();
                    return requirements.WithVersion( v ).WithLevel( level );
                }
                Debug.Assert( requirements.Level == ReleaseLevel.Feature );
                // It is already a feature. For true pre-release, the feature/breaking changes is
                // not relevant so we let it go. However for the 0 Major this is important.
                if( v.IsPrerelease )
                {
                    m.Info( $"Version automatically infered: {v}." );
                    return requirements.WithVersion( v );
                }
                // Edge case of the 0 Major.
                level = versionSelector.GetZeroMajorSingleVersionFeatureActualLevel( m, Solution, v );
                if( level == ReleaseLevel.None ) return _releaseInfo;
                if( level == ReleaseLevel.Fix ) throw new InvalidOperationException( "GetZeroMajorSingleVersionFeatureActualLevel can not return Fix level." );
                return requirements.WithVersion( v ).WithLevel( level );
            }
            // We are on an Official (non pre-release version) and on a fix or a feature.
            // We can automatically infer the release level from this only version.
            if( v.Minor == 0 && v.Patch == 0 ) requirements = requirements.WithLevel( ReleaseLevel.BreakingChange );
            else if( v.Patch == 0 ) requirements = requirements.WithLevel( ReleaseLevel.Feature );
            m.Info( $"Version automatically inferred: {v}." );
            return requirements.WithVersion( v );
        }

        /// <summary>
        /// Filters a set of versions according to a <see cref="ReleaseConstraint"/>.
        /// </summary>
        /// <param name="possibleVersions">Set of versions to filter.</param>
        /// <param name="c">The release constraint.</param>
        /// <returns>Filtered set of versions.</returns>
        public static IEnumerable<CSVersion> FilterVersions( IEnumerable<CSVersion> possibleVersions, ReleaseConstraint c, bool finalLevelIsKnown )
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
            else if( finalLevelIsKnown )
            {
                // When there is no breaking change nor feature, this is necessarily a Patch.
                filtered = filtered.Where( v => v.IsPatch );
            }

            // 3 - On a breaking change, Official version must have their Major bumped (ie. their Minor and Patch must be 0).
            //     The 0 major is excluded from this filter.
            if( (c & ReleaseConstraint.HasBreakingChanges) != 0 )
            {
                filtered = filtered.Where( v => v.Major == 0 || v.IsPrerelease || (v.Minor == 0 && v.Patch == 0) );
            }
            return filtered;
        }

        internal XElement ToXml()
        {
            return new XElement( "S",
                        new XAttribute( "Name", Solution.UniqueSolutionName ),
                        new XAttribute( "CommitSha", _commitVersionInfo.CommitSha ),
                        _releaseInfo.ToXml(),
                        new XElement( "UpdatePackageInfos",
                               _updatePackageInfos.Where( p => p.SolutionName == Solution.UniqueSolutionName ).Select( p => p.ToXml( false ) ),
                               new XElement( "SecondarySolution",
                                    _updatePackageInfos.Where( p => p.SolutionName != Solution.UniqueSolutionName ).Select( p => p.ToXml( true ) ) ) ),
                        new XElement( "ReleaseNote", new XCData( ReleaseNote ) ));
        }
    }
}
