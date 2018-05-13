using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    public class SolutionReleaser
    {
        readonly List<(DeclaredPackageDependency Dep, SVersion Version)> _packageVersionUpdate;
        GlobalReleaser _releaser;

        ReleaseInfo _releaseInfo;
        bool _releaseInfoAvailable;

        internal SolutionReleaser(
            SolutionDependencyResult.DependentSolution solution,
            RepositoryInfo info )
        {
            Debug.Assert( info != null && info.PossibleVersions != null );
            Solution = solution;
            RepositoryInfo = info;
            _packageVersionUpdate = new List<(DeclaredPackageDependency Dep, SVersion Version)>();
        }

        /// <summary>
        /// Gets the global <see cref="SolutionDependencyResult"/> of the <see cref="DependentSolution"/>.
        /// </summary>
        public SolutionDependencyResult GlobalResult => Solution.GlobalResult;

        /// <summary>
        /// Gets the current <see cref="ReleaseInfo"/>: the result of the last <see cref="EnsureReleaseInfo"/> call.
        /// May not be valid.
        /// </summary>
        public ReleaseInfo CurrentReleaseInfo => _releaseInfo;

        /// <summary>
        /// This is the heart of the global releaser system.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="versionSelector">The version selector.</param>
        /// <returns>A <see cref="ReleaseInfo"/> that can be invalid.</returns>
        public ReleaseInfo EnsureReleaseInfo( IActivityMonitor m, IReleaseVersionSelector versionSelector, Action<SolutionReleaser> collector = null )
        {
            if( _releaseInfoAvailable ) return _releaseInfo;
            _releaseInfoAvailable = true;
            if( UpdateReleaseInfo( m, versionSelector ).IsValid && collector != null ) collector( this );
            return _releaseInfo;
        }

        ReleaseInfo UpdateReleaseInfo( IActivityMonitor m, IReleaseVersionSelector versionSelector )
        {
            _packageVersionUpdate.Clear();
            ReleaseInfo requirements = new ReleaseInfo();
            // Here we could have processed only the MinimalRequirements (so that the global process ordering
            // matches the dependency order).
            // But, we need to iterate though all Requirements in order to not miss a version
            // package upgrade.
            foreach( var s in Solution.Requirements )
            {
                var sInfo = _releaser.FindBySolution( s ).EnsureReleaseInfo( m, versionSelector );
                if( !sInfo.IsValid ) return _releaseInfo;
                requirements = requirements.CombineRequirement( sInfo );

                var updates = GlobalResult.ProjectDependencies.DependencyTable
                                .Where( r => !r.IsExternalDependency
                                             && r.SourceProject.Project.PrimarySolution == Solution.Solution
                                             && r.TargetPackage.Project.PrimarySolution == s.Solution
                                             && r.Version != sInfo.Version )
                                .Select( p => (p.RawPackageDependency, (SVersion)sInfo.Version) );

                _packageVersionUpdate.AddRange( updates );
            }
            // Handle package references updates.
            //  - if none and a Release tag already exists on the commit point, there
            //    is nothing to do (we exit directly with the only ReleaseLevel.None possible).
            //  - For all other cases, we compute the right AllPossibleVersions and adjust the Release
            //    level of the requirements.
            if( _packageVersionUpdate.Count > 0 )
            {
                // Since we'll need a commit to upgrade the package versions.
                AllPossibleVersions = RepositoryInfo.NextPossibleVersions;
                if( requirements.Level == ReleaseLevel.None )
                {
                    // Our packages need an update but dependent solutions did not need to build...
                    // This may happen if our package versions just need to be fixed: this is a perfectly
                    // valid scenario.
                    // So what are the choices of the user in such case?
                    // None is not an option: the updated dependencies may break something here. At least a fix must be released.
                    requirements = requirements.WithLevel( ReleaseLevel.Fix );
                }
                Debug.Assert( requirements.Level != ReleaseLevel.None );
            }
            else
            {
                // No need to upgrade this Solution to update its packages references.
                if( requirements.Level != ReleaseLevel.None )
                {
                    // This is odd... A dependency is a fix (or more). Its own version must have been bumped
                    // and so there should have been an impact on our package versions.
                    // (Otherwise this would mean that the solution dependency is wrong!)
                    throw new Exception( $"{Solution.Solution.UniqueSolutionName}: A dependent solution implied a ReleaseLevel of {requirements.Level} but none of our packages had to be updated." );
                }
                Debug.Assert( requirements.Level == ReleaseLevel.None );
                // If a tag exists on the commit point:
                //   1 - We must use it.
                //   2 - There is nothing more to do since it has already been published.
                // We must exit here directly since the code below does not cover this edge case:
                // when there is only one possible version, the code below will handle/ask release level
                // and here we must keep the ReleaseLevel.None.
                if( RepositoryInfo.ValidReleaseTag != null )
                {
                    AllPossibleVersions = new[] { RepositoryInfo.ValidReleaseTag };
                    return _releaseInfo = requirements.WithVersion( RepositoryInfo.ValidReleaseTag );
                }
                // We are on a commit point that has no tag.
                // The new code that is contained in this commit MUST be released at least as a fix.
                requirements = requirements.WithLevel( ReleaseLevel.Fix );
                AllPossibleVersions = RepositoryInfo.PossibleVersions;
            }
            Debug.Assert( requirements.Level >= ReleaseLevel.Fix );

            // Handles the versions now.
            if( AllPossibleVersions.Count == 0 )
            {
                m.Error( "No PossibleVersions exist for this commit point according to SimpleGitVersion." );
                return _releaseInfo;
            }

            var filteredVersions = FilterVersions( AllPossibleVersions, requirements.Constraint, false ).ToList();
            if( filteredVersions.Count == 0 )
            {
                m.Error( $"No PossibleVersions exist for this commit point with Constraint = {requirements.Constraint}." );
                return _releaseInfo;
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
            //// Multiple versions are possible.
            //// 1 - First choose between Official and Preleases if both are possible.
            //m.Info( $"Choosing version for {Solution.Solution.UniqueSolutionName} among {filteredVersions.Count} possible versions." );
            //if( (requirements.Constraint & ReleaseConstraint.MustBePreRelease) == 0 )
            //{
            //    var officials = filteredVersions.Where( v => !v.IsPreRelease );
            //    var prereleases = filteredVersions.Where( v => v.IsPreRelease );
            //    if( officials.Any() && prereleases.Any() )
            //    {
            //        var choice = versionSelector.ChooseBetweenOfficialAndPreReleaseVersions( m, Solution, officials, prereleases );
            //        if( choice == null ) return _releaseInfo;
            //        filteredVersions = choice.ToList();
            //    }
            //}
            //if( filteredVersions.Count == 1 )
            //{
            //    return HandleSingleVersion( m, versionSelector, requirements, filteredVersions[0] );
            //}
            Debug.Assert( filteredVersions.Count > 1, "There are still multiple versions." );
            var finalVersion = versionSelector.ChooseFinalVersion( m, Solution, filteredVersions, requirements );
            if( finalVersion == null ) return _releaseInfo;
            if( !filteredVersions.Contains( finalVersion ) ) throw new InvalidOperationException( "ChooseFinalVersion must return one of the possible versions." );
            return _releaseInfo = requirements.WithVersion( finalVersion );
        }

        ReleaseInfo HandleSingleVersion( IActivityMonitor m, IReleaseVersionSelector versionSelector, ReleaseInfo requirements, CSVersion v )
        {
            if( requirements.Level == ReleaseLevel.BreakingChange )
            {
                // The version is a breaking change and the requirements.Level is actually
                // a breaking change, this is fine.
                m.Info( $"Version automatically inferred: {v}." );
                return _releaseInfo = requirements.WithVersion( v );
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
                    if( level == ReleaseLevel.None ) return _releaseInfo;
                    return _releaseInfo = requirements.WithVersion( v ).WithLevel( level );
                }
                Debug.Assert( requirements.Level == ReleaseLevel.Feature );
                // It is already a feature. For true pre-release, the feature/breaking changes is
                // not relevant so we let it go. However for the 0 Major this is important.
                if( v.IsPrerelease )
                {
                    m.Info( $"Version automatically infered: {v}." );
                    return _releaseInfo = requirements.WithVersion( v );
                }
                // Edge case of the 0 Major.
                level = versionSelector.GetZeroMajorSingleVersionFeatureActualLevel( m, Solution, v );
                if( level == ReleaseLevel.None ) return _releaseInfo;
                if( level == ReleaseLevel.Fix ) throw new InvalidOperationException( "GetZeroMajorSingleVersionFeatureActualLevel can not return Fix level." );
                return _releaseInfo = requirements.WithVersion( v ).WithLevel( level );
            }
            // We are on an Official (non pre-release version) and on a fix or a feature.
            // We can automatically infer the release level from this only version.
            if( v.Minor == 0 && v.Patch == 0 ) requirements = requirements.WithLevel( ReleaseLevel.BreakingChange );
            else if( v.Patch == 0 ) requirements = requirements.WithLevel( ReleaseLevel.Feature );
            m.Info( $"Version automatically inferred: {v}." );
            return _releaseInfo = requirements.WithVersion( v );
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

        /// <summary>
        /// Gets whether this solution requires package updates.
        /// This is always false until <see cref="EnsureReleaseInfo"/> has been called.
        /// </summary>
        public bool NeedPackageReferenceUpdates => _packageVersionUpdate.Count > 0;

        internal XElement ToXml( bool addBuild )
        {
            Debug.Assert( _releaseInfoAvailable && _releaseInfo.IsValid );
            return new XElement( "S",
                        new XAttribute( "Name", Solution.Solution.UniqueSolutionName ),
                        new XAttribute( "Version", _releaseInfo.Version ),
                        new XElement( "ReleaseInfo", new XAttribute( "Level", _releaseInfo.Level ), new XAttribute( "Constraint", _releaseInfo.Constraint ) ),
                        new XElement( "Upgrades", PackageVersionUpdateToXml( Solution.Solution ) ),
                        addBuild ? new XElement( "Build" ) : null );

            IEnumerable<XElement> PackageVersionUpdateToXml( Solution primary )
            {
                return _packageVersionUpdate
                            .Where( row => row.Dep.Owner.Solution == primary )
                            .Select( p => ToUpgradeXml( p.Dep, p.Version ) )
                            .Concat( _packageVersionUpdate
                                         .Where( r => r.Dep.Owner.Solution != primary )
                                         .GroupBy( r => r.Dep.Owner.Solution )
                                         .Select( g => new XElement( "SecondarySolution",
                                                        new XAttribute( "Name", g.Key.UniqueSolutionName ),
                                                        g.Select( p => ToUpgradeXml( p.Dep, p.Version ) ) ) ) ); 

                XElement ToUpgradeXml( DeclaredPackageDependency dep, SVersion version )
                {
                    return new XElement( "Upgrade",
                            new XAttribute( "Project", dep.Owner.Path ),
                            new XAttribute( "PackageId", dep.PackageId ),
                            new XAttribute( "Version", version ) );
                }
            }

        }

        /// <summary>
        /// Gets the global releaser.
        /// </summary>
        public GlobalReleaser Releaser => _releaser;

        internal void Initialize( GlobalReleaser r )
        {
            _releaser = r;
        }

        /// <summary>
        /// Gets the solution.
        /// </summary>
        public SolutionDependencyResult.DependentSolution Solution { get; }

        /// <summary>
        /// Gets the SimplegitVersion <see cref="RepositoryInfo"/>.
        /// </summary>
        public RepositoryInfo RepositoryInfo { get; }

        /// <summary>
        /// Gets all possible versions from SimpleGitVersion <see cref="RepositoryInfo"/>. 
        /// </summary>
        public IReadOnlyList<CSVersion> AllPossibleVersions { get; private set; }
    }
}
