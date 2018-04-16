using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XGlobalDependencyAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly IntParameter _choice;
        readonly IssueCollector _issueCollector;
        readonly FileSystem _fileSystem;
        readonly XKnownPackageVersionsIssuer _knownPackageVersions;

        public XGlobalDependencyAction(
            Initializer intializer,
            FileSystem fileSystem,
            ActionCollector collector,
            IssueCollector issueCollector,
            XSolutionCentral solutions,
            XKnownPackageVersionsIssuer knownPackageVersions = null )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _issueCollector = issueCollector;
            _knownPackageVersions = knownPackageVersions;
            _solutions = solutions;
            const string menu =
@"--- Solution Dependencies
   1 - PublishedProjects - Consider only published projects of primary solutions
                           (secondary solutions are ignored).
   2 - PublishedAndTestsProjects - Consider published and tests projects of primary solutions
                                   (secondary solutions are ignored).
   3 - EverythingExceptBuildProjects - Consider all projects and the secondary solutions if any.
                                       Build projects are ignored.
--- Project Dependencies
   4 - Dumps all projects dependencies across all solutions.
   5 - Dumps version dependency discrepancies across all solutions.
   6 - Generate issues to fix version dependency discrepancies across all solutions.
";
            _choice = AddIntParameter( "choice", menu, ( m, i ) =>
            {
                if( i < 1 || i > 6 )
                {
                    m.Error( "Must be between 1 and 6." );
                    return false;
                }
                return true;
            } );
        }

        public override bool Run( IActivityMonitor m )
        {
            var all = _solutions.AllSolutions.Select( s => s.Solution );
            var deps = DependencyContext.Create( m, all );
            if( deps == null ) return false;
            switch( _choice.Value )
            {
                case 1: 
                    DisplayResult( m, deps.AnalyzeDependencies( m, SolutionSortStrategy.PublishedProjects ) );
                    break;
                case 2:
                    DisplayResult( m, deps.AnalyzeDependencies( m, SolutionSortStrategy.PublishedAndTestsProjects ) );
                    break;
                case 3:
                    DisplayResult( m, deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects ) );
                    break;
                case 4:
                    DisplayProjectDependencies( m, "Dumping all project dependencies.", deps.ProjectDependencies.PerFrameworkDependencies );
                    break;
                case 5:
                    DisplayProjectDependencies( m, "Project version dependency discrepancies.", deps.ProjectDependencies.VersionDiscrepancies );
                    break;
                case 6:
                    CreateIssues( m, deps );
                    break;
            }
            return true;
        }

        void CreateIssues( IActivityMonitor m, DependencyContext deps )
        {
            _issueCollector.ClearIssues( m, i => i.Identifier.StartsWith( "ProjectVersionDeps:" ) );

            var toFix = deps.ProjectDependencies.VersionDiscrepancies
                            .SelectMany( d =>
                                    d.DependencyTable
                                        .GroupBy( r => r.PackageId )
                                        .Select( g => (PackageName: g.Key,
                                                        MaxVer: g.Select( x => x.Version ).Max(),
                                                        Rows: g) )
                                        .Select( t => (d.Framework,
                                                        t.PackageName,
                                                        t.MaxVer,
                                                        Rows: t.Rows.Where( x => x.Version != t.MaxVer ).ToList()) ) )
                            .ToList();

            foreach( var fix in toFix )
            {
                _issueCollector.RunIssueFactory( m, builder =>
                {
                    var allProjectNames = fix.Rows.Select( r => r.SourceProject.FullName ).Concatenate();
                    builder.Monitor.Info( $"Package {fix.PackageName} in {fix.Framework} should be upgraded to {fix.MaxVer} for {allProjectNames}." );
                    builder.CreateIssue( $"ProjectVersionDeps:{allProjectNames}", $"Fix version dependency to {fix.PackageName} in {fix.Framework}.", monitor =>
                    {
                        foreach( var f in fix.Rows )
                        {
                            f.RawPackageDependency.Owner.SetPackageReferenceVersion( m, fix.Framework, fix.PackageName, fix.MaxVer );
                            f.RawPackageDependency.Owner.Save( m, _fileSystem );
                        }
                        return true;
                    } );
                    return true;
                } );
            }
        }

        void DisplayProjectDependencies(
            IActivityMonitor m,
            string title,
            IReadOnlyList<ProjectDependencyResult.FrameworkDependencies> projectDependencies )
        {
            using( m.OpenInfo( title ) )
            {
                foreach( var deps in projectDependencies )
                {
                    using( m.OpenInfo( $"TargetFramework = {deps.Framework}" ) )
                    {
                        foreach( var d in deps.DependencyTable.GroupBy( r => r.PackageId ) )
                        {
                            using( m.OpenInfo( $"{d.Key} - {d.Select( r => r.Version ).Distinct().Count()} versions." ) )
                            {
                                foreach( var final in d.GroupBy( r => r.Version ) )
                                {
                                    m.Info( $"{final.Key} <- {final.Select( r => r.SourceProject.FullName ).Concatenate()}" );
                                }
                            }
                        }
                    }
                }
            }
        }

        internal static void DisplayResult( IActivityMonitor m, SolutionDependencyResult result )
        {
            using( m.OpenInfo( $"Solutions sorted ({result.Content}): " ) )
            {
                if( result.HasError ) result.RawSorterResult.LogError( m );
                else
                {
                    var display = result.DependencyTable
                                    .Select( r => (Head: (r.Index, r.Solution), r) )
                                    .GroupBy( r2 => r2.Head )
                                    .Select( g => (
                                            g.Key.Index,
                                            g.Key.Solution,
                                            Targets: g.GroupBy( r => r.r.Target?.Solution )
                                                      .Where( t => t.Key != null )
                                                      .Select( t => (
                                                            Target: t.Key,
                                                            Causes: t.GroupBy( r => r.r.Target )
                                                                     .Select( r => (
                                                                        ProjectTarget: r.Key,
                                                                        References: r.Select( o => o.r.Origin )
                                                                        ) )
                                                            ) )
                                            ) );
                    StringBuilder b = new StringBuilder();
                    foreach( var s in display )
                    {
                        b.Append( s.Index ).Append( " - " ).Append( s.Solution.UniqueSolutionName );
                        if( s.Targets.Any() )
                        {
                            b.Append( " => " ).AppendStrings( s.Targets.Select( t => t.Target.UniqueSolutionName ) );
                        }
                        b.AppendLine();
                        foreach( var t in s.Targets )
                        {
                            b.Append( "    - " ).AppendLine( t.Target.UniqueSolutionName );
                            foreach( var c in t.Causes )
                            {
                                b.Append( "        " )
                                 .Append( c.ProjectTarget.Name )
                                 .Append( " <- " )
                                 .AppendStrings( c.References.Select( o => o.Name ) )
                                 .AppendLine();
                            }
                        }
                    }
                    m.Info( b.ToString() );
                }
            }
        }
    }
}
