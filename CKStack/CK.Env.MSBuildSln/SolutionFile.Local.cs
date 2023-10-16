using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;
using CK.Build;
using System.Runtime.Intrinsics.Arm;

namespace CK.Env.MSBuildSln
{
    public sealed partial class SolutionFile
    {
        public int TransformToLocal( IActivityMonitor monitor,
                                     Func<Artifact, MSProject?> selector,
                                     Func<MSProject, IEnumerable<MSProject>> projectDependencies )
        {
            var collector = new HashSet<MSProject>();
            foreach( var p in MSProjects )
            {
                p.TransformPackageToProjectDepencies( monitor, selector, collector );
            }
            if( collector.Count > 0 )
            {
                var local = EnsureLocalFolder();
                var originals = collector.ToArray();
                collector.Clear();
                foreach( var p in originals )
                {
                    if( collector.Add( p ) )
                    {
                        AddDependencies( collector, projectDependencies, p );
                    }

                    static void AddDependencies( HashSet<MSProject> collector, Func<MSProject, IEnumerable<MSProject>> projectDependencies, MSProject p )
                    {
                        foreach( var dep in projectDependencies( p ) )
                        {
                            if( collector.Add( dep ) )
                            {
                                AddDependencies( collector, projectDependencies, dep );
                            }
                        }
                    }
                }
                var originFile = MSProject.RemoveBranchParts( this, FilePath );
                foreach( var project in collector )
                {
                    var relative = MSProject.GetRelativePath( originFile, project );
                    var projectName = relative.LastPart;
                    var t = ProjectType.FromFilePath( ref projectName );
                    if( t == KnownProjectType.Unknown )
                    {
                        monitor.Warn( $"Unhandled project type '{projectName}'. Ignored." );
                    }
                    else
                    {
                        var p = new StackLocalProject( local, t, projectName, relative );
                        AddProject( p );
                    }
                }
            }
            return collector.Count;
        }

        SolutionFolder EnsureLocalFolder()
        {
            SolutionFolder? local = _projectBaseList.OfType<SolutionFolder>()
                                        .FirstOrDefault( s => "$Local".Equals( s.ProjectName, StringComparison.OrdinalIgnoreCase ) );
            if( local == null )
            {
                local = new SolutionFolder( this, Guid.NewGuid().ToString( "B" ), "$Local" );
                AddProject( local );
            }
            return local;
        }
    }
}
