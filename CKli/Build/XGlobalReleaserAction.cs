using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SimpleGitVersion;
using CSemVer;

namespace CKli
{
    public class XGlobalReleaserAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;
        readonly XPublishedPackageFeeds _localPackages;

        public XGlobalReleaserAction(
            Initializer intializer,
            FileSystem fileSystem,
            ActionCollector collector,
            XPublishedPackageFeeds localPackages,
            XSolutionCentral solutions )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _solutions = solutions;
            _localPackages = localPackages;
        }


        public override bool Run( IActivityMonitor m )
        {
            // Consider all GitFolders that contains at least a solution definition in 'develop' branch.
            var gitFolders = _solutions.AllDevelopSolutions.Select( s => s.GitBranch.Parent.GitFolder )
                                .Distinct()
                                .Select( g => ( GitFolder: g, Info: g.GetVersionRepositoryInfo( m )) )
                                .ToList();
            if( gitFolders.Any( g => g.Info == null ) ) return false;

            var deps = DependencyContext.Create( m, _solutions.AllDevelopSolutions.Select( x => x.Solution ) );
            if( deps == null ) return false;
            SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
            if( r.HasError ) r.RawSorterResult.LogError( m );
            else
            {
                var solutionsToReleaseInfo = r.Solutions
                                                .Select( s => (S: s, G: gitFolders.First( g => g.GitFolder.SubPath == s.Solution.SolutionFolderPath )) )
                                                .ToDictionary( s => s.S.Solution, s => new SolutionReleaser( s.G.GitFolder, s.S, s.G.Info ) );
                foreach( var s in r.Solutions )
                {
                    // A solution can have no published package.
                    var p = s.Solution.PublishedProjects.FirstOrDefault();
                    if( p != null )
                    {
                        var vOfficial = _localPackages.GetMyGetLastVersion( m, "invenietis-release", p.Name );
                        var vPreview = _localPackages.GetMyGetLastVersion( m, "invenietis-preview", p.Name );
                        solutionsToReleaseInfo[s.Solution].BestPublishedOfficialVersion = vOfficial;
                    }
                }
            }

            return true;
        }


    }


}
