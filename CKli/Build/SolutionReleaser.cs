using CK.Core;
using CK.Env;
using CK.Env.MSBuild;
using CSemVer;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class SolutionReleaser
    {
        readonly List<(DeclaredPackageDependency, CSVersion)> _packageVersionUpdate;
        GlobalReleaser _releaser;

        ReleaseInfo _releaseInfo;
        bool _releaseInfoAvailable;

        internal SolutionReleaser(
            GitFolder g,
            SolutionDependencyResult.DependentSolution solution,
            RepositoryInfo info
            )
        {
            Debug.Assert( info != null  && info.PossibleVersions != null );
            GitFolder = g;
            Solution = solution;
            RepositoryInfo = info;
            _packageVersionUpdate = new List<(DeclaredPackageDependency, CSVersion)>();
        }

        /// <summary>
        /// Gets the global <see cref="SolutionDependencyResult"/> of the <see cref="DependentSolution"/>.
        /// </summary>
        public SolutionDependencyResult GlobalResult => Solution.GlobalResult;

        public ReleaseInfo EnsureReleaseInfo( IActivityMonitor m, ReleaseVersionSelector versionSelector )
        {
            if( _releaseInfoAvailable ) return _releaseInfo;
            _releaseInfoAvailable = true;
            ReleaseInfo requirements = new ReleaseInfo();
            foreach( var s in Solution.TransitiveRequirements )
            {
                var info = _releaser.FindBySolution( s ).EnsureReleaseInfo( m, versionSelector );
                if( !info.IsValid ) return _releaseInfo;
                requirements = requirements.CombineRequirement( info );

                _packageVersionUpdate.AddRange( GlobalResult.DependencyTable
                                                    .Where( r => r.Origin?.PrimarySolution == Solution.Solution
                                                                 && r.Target.PrimarySolution == s.Solution )
                                                    .SelectMany( r => r.Target.Deps.Packages )
                                                    .Where( p => p.Version.ToString() != info.Version.ToString( CSVersionFormat.ShortForm ) )
                                                    .Select( p => (p,info.Version) ) );
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
                    // None is not an option: the updated dependencies may break something here. A fix must be released.
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
                requirements.WithLevel( ReleaseLevel.Fix );
                AllPossibleVersions = RepositoryInfo.PossibleVersions;
            }
            Debug.Assert( requirements.Level >= ReleaseLevel.Fix );

            // Handles the versions now.
            if( AllPossibleVersions.Count == 0 )
            {
                m.Error( "No PossibleVersions exist for this commit point according to SimpleGitVersion." );
                return _releaseInfo;
            }

            var vForRequirements = GetPossibleVersions( requirements.Constraint ).ToList();
            if( vForRequirements.Count == 0 )
            {
                m.Error( $"No PossibleVersions exist for this commit point with Constraint = {requirements.Constraint}." );
                return _releaseInfo;
            }
            if( vForRequirements.Count == 1 )
            {
                var v = vForRequirements[0];
                if( requirements.Level == ReleaseLevel.BreakingChange )
                {
                    // The version is a breaking change and the requirements.Level is actually
                    // a breaking change, this is fine.
                    m.Info( $"Version automatically infered: {v}." );
                    return _releaseInfo = requirements.WithVersion( v );
                }
                // There is only one version and we are not on a breaking change propagation.
                // The risk here is to release a version that does not indicate the correct type of change to dependent
                // solutions.
                // If the version is a pre release then all dependent versions will also be prereleases
                // Pre-releases don't care about breaking changes however they care about
                // fixes vs. features or breaking changes for automatic computation.
                if( v.IsPreRelease )
                {
                    if( requirements.Level == ReleaseLevel.Fix )
                    {
                        var level = versionSelector.GetPreReleaseSingleVersionFixActualLevel( m, Solution, v );
                        if( level == ReleaseLevel.None ) return _releaseInfo;
                        return _releaseInfo = requirements.WithVersion( v ).WithLevel( level );
                    }
                    Debug.Assert( requirements.Level == ReleaseLevel.Feature );
                    // It is already a feature. Since we are on pre-release, and the feature/breaking changes is
                    // not relevant, we let it go.
                    m.Info( $"Version automatically infered: {v}." );
                    return _releaseInfo = requirements.WithVersion( v );
                }
                // We are on an Official (non pre-release version) and on a fix or a feature.
                // We can automatically infer the release level from this only version.
                if( v.Minor == 0 && v.Patch == 0 ) requirements = requirements.WithLevel( ReleaseLevel.BreakingChange );
                else if( v.Patch == 0 ) requirements = requirements.WithLevel( ReleaseLevel.Feature );
                m.Info( $"Version automatically infered: {v}." );
                return _releaseInfo = requirements.WithVersion( v );
            }
            // Multiple versions are possible.
            return _releaseInfo = versionSelector.ChooseVersion( m, Solution, vForRequirements, requirements );
        }

        /// <summary>
        /// Gets whether this solution requires package updates.
        /// This makes sense only once <see cref="EnsureReleaseInfo"/> has been called.
        /// </summary>
        public bool NeedPackageReferenceUpdates => _packageVersionUpdate.Count > 0; 

        /// <summary>
        /// Gets the global releaser.
        /// </summary>
        public GlobalReleaser Releaser => _releaser;

        internal void Initialize( GlobalReleaser r )
        {
            _releaser = r;
        }

        /// <summary>
        /// Get the GitFolder to which this solution belongs.
        /// </summary>
        public GitFolder GitFolder;

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

        /// <summary>
        /// Filters <see cref="AllPossibleVersions"/> according to a <see cref="ReleaseConstraint"/>.
        /// </summary>
        /// <param name="c">The release constraint.</param>
        /// <returns>Filtered set of versions.</returns>
        public IEnumerable<CSVersion> GetPossibleVersions( ReleaseConstraint c )
        {
            var all = AllPossibleVersions;

            return NewMethod( c, all );
        }

    }

}
