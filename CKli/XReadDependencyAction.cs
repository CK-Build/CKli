using CK.Core;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XReadDependencyAction : XAction
    {
        readonly XSolutionCentral _solutions;

        public XReadDependencyAction(
            Initializer intializer,
            ActionCollector collector,
            XSolutionCentral solutions )
            : base( intializer, collector )
        {
            _solutions = solutions;
        }

        public override bool Run( IActivityMonitor m )
        {
            var all = _solutions.LoadAllSolutions( m, false );
            if( all == null ) return false;
            var deps = DependencyContext.Create( m, all );
            if( deps == null ) return false;
            DisplayResult( m, deps.AnalyzeDependencies( m, SolutionSortStrategy.PublishedProjects ) );
            DisplayResult( m, deps.AnalyzeDependencies( m, SolutionSortStrategy.PublishedAndTestsProjects ) );
            DisplayResult( m, deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects ) );
            return true;
        }

        private static void DisplayResult( IActivityMonitor m, SolutionDependencyResult result )
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
