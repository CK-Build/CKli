using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Template method pattern to handle whole stack build.
    /// </summary>
    abstract class Builder
    {
        protected readonly Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> DriverFinder;
        readonly BuildResultType _type;
        readonly ISolutionDriver[] _drivers;
        readonly Dictionary<string, SVersion> _packagesVersion;
        readonly List<UpdatePackageInfo>[] _upgrades;
        readonly SVersion[] _targetVersions;
        readonly ArtifactCenter _artifacts;

        protected Builder(
            BuildResultType type,
            ArtifactCenter artifacts,
            IDependentSolutionContext ctx,
            Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder )
        {
            if( ctx == null ) throw new ArgumentNullException( nameof( ctx ) );
            if( driverFinder == null ) throw new ArgumentNullException( nameof( driverFinder ) );
            if( artifacts == null ) throw new ArgumentNullException( nameof( artifacts ) );
            _type = type;
            _artifacts = artifacts;
            DependentSolutionContext = ctx;
            DriverFinder = driverFinder;
            _packagesVersion = new Dictionary<string, SVersion>();
            _upgrades = new List<UpdatePackageInfo>[ctx.Solutions.Count];
            _targetVersions = new SVersion[ctx.Solutions.Count];
            _drivers = new ISolutionDriver[ctx.Solutions.Count];
        }

        /// <summary>
        /// Gets the dependent solution context.
        /// </summary>
        public IDependentSolutionContext DependentSolutionContext { get; private set; }

        /// <summary>
        /// Runs the build. Orchestrates calls to <see cref="PrepareBuild"/> and <see cref="Build"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The BuildResult on success, null on error.</returns>
        public BuildResult Run( IActivityMonitor m )
        {
            if( _packagesVersion.Count > 0 ) throw new InvalidOperationException();

            using( m.OpenDebug( "Before preparing builds." ) )
            {
                var newdDepContext = OnBeforePrepareBuild( m );
                if( newdDepContext == null ) return null;
                DependentSolutionContext = newdDepContext;
            }
            BuildResult result = null;
            using( m.OpenInfo( "Preparing builds." ) )
            {
                if( !RunPrepareBuild( m ) )
                {
                    m.CloseGroup( "Failed." );
                    OnPrepareBuildFailed( m );
                    return null;
                }
                result = BuildResult.Create( m, _type, _artifacts, DependentSolutionContext.Solutions, _targetVersions, GetReleaseNotes() );
                if( result == null ) return null;
                m.CloseGroup( "Success." );
            }
            using( m.OpenDebug( "Before running builds." ) )
            {
                if( !OnBeforeBuild( m, result ) ) return null;
            }
            using( m.OpenInfo( "Running builds." ) )
            {
                if( !RunBuild( m ) )
                {
                    m.CloseGroup( "Failed." );
                    OnBuildFailed( m );
                    return null;
                }
                m.CloseGroup( "Success." );
            }
            return OnBuildSuccess( m, result );
        }

        protected virtual IReadOnlyList<ReleaseNoteInfo> GetReleaseNotes()
        {
            return null;
        }

        protected virtual IDependentSolutionContext OnBeforePrepareBuild( IActivityMonitor m )
        {
            return DependentSolutionContext;
        }

        protected virtual bool OnBeforeBuild( IActivityMonitor m, BuildResult result )
        {
            return true;
        }

        protected virtual void OnPrepareBuildFailed( IActivityMonitor m )
        {
        }

        protected virtual void OnBuildFailed( IActivityMonitor m )
        {
        }

        protected virtual BuildResult OnBuildSuccess( IActivityMonitor m, BuildResult result )
        {
            return result;
        }

        bool RunPrepareBuild( IActivityMonitor m )
        {
            var solutions = DependentSolutionContext.Solutions;
            for( int i = 0; i < solutions.Count; ++i )
            {
                var s = solutions[i];
                var driver = _drivers[i] = DriverFinder( m, DependentSolutionContext, s.UniqueSolutionName );
                var upgrades = s.ImportedLocalPackages
                                .Select( p => new UpdatePackageInfo(
                                                    s.UniqueSolutionName,
                                                    p.ProjectName,
                                                    p.Package.PackageId,
                                                    _packagesVersion[p.Package.PackageId] ) )
                                .ToList();
                _upgrades[i] = upgrades;
                using( m.OpenInfo( $"Preparing {s} build." ) )
                {
                    var pr = PrepareBuild( m, s, driver, upgrades );
                    if( pr.Version == null ) return false;
                    m.CloseGroup( $"Target version: {pr.Version}{(pr.MustBuild ? "" :" (no build required)")}" );
                    _targetVersions[i] = pr.MustBuild ? pr.Version : null;
                    _packagesVersion.AddRange( s.GeneratedPackages.Select( p => new KeyValuePair<string, SVersion>( p.Name, pr.Version ) ) );
                }
            }
            return true;
        }

        bool RunBuild( IActivityMonitor m )
        {
            var solutions = DependentSolutionContext.Solutions;
            for( int i = 0; i < solutions.Count; ++i )
            {
                var s = solutions[i];
                DependentSolutionContext.LogSolutions( m, s );
                var buildProjectsUpgrade = DependentSolutionContext.BuildProjectsInfo
                                             .Where( z => z.SolutionName == s.UniqueSolutionName )
                                             .SelectMany( z => z.UpgradePackages
                                                   .Select( packageName => new UpdatePackageInfo(
                                                       s.UniqueSolutionName,
                                                       z.ProjectName,
                                                       packageName,
                                                       _packagesVersion[packageName] ) ) );
                using( m.OpenInfo( $"Running {s} build." ) )
                {
                    // _targetVersions[i] is null if build must not be done.
                    if( !Build( m, solutions[i], _drivers[i], _upgrades[i], _targetVersions[i], buildProjectsUpgrade ) ) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Must prepare the build (typically by applying the <paramref name="upgrades"/>) and returns
        /// a version for the solution (or null on error).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="s">The solution.</param>
        /// <param name="driver">The solution driver.</param>
        /// <param name="upgrades">The set of required package upgrades.</param>
        /// <returns>The version (or null if an error occurred) and whether the build must be actually done or skipped.</returns>
        protected abstract (SVersion Version, bool MustBuild) PrepareBuild( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades );

        /// <summary>
        /// Builds the solution.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="s">The solution.</param>
        /// <param name="driver">The solution driver.</param>
        /// <param name="upgrades">The set of required package upgrades.</param>
        /// <param name="sVersion">The version computed by <see cref="PrepareBuild"/>.</param>
        /// <param name="buildProjectsUpgrade">The build projects upgrades.</param>
        /// <returns>True on success, false on error.</returns>
        protected abstract bool Build( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IEnumerable<UpdatePackageInfo> buildProjectsUpgrade );

    }
}
