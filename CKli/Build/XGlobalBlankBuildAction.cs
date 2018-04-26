using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using CKSetup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli
{
    public class XGlobalBlankBuildAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;
        readonly XPublishedPackageFeeds _localPackages;

        public XGlobalBlankBuildAction(
            Initializer intializer,
            FileSystem fileSystem,
            XPublishedPackageFeeds localPackages,
            ActionCollector collector,
            XSolutionCentral solutions )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _solutions = solutions;
            _localPackages = localPackages;
        }

        public override bool Run( IActivityMonitor m )
        {
            using( m.OpenTrace( $"Blank build of {_solutions.AllDevelopSolutions.Count} solutions in {_solutions.AllGitFoldersWithDevelopBranchName.Count} git folders." ) )
            {
                foreach( var g in _solutions.AllGitFoldersWithDevelopBranchName )
                {
                    if( !g.GitFolder.SwitchFromDevelopToBlankDev( m ) ) return false;
                }
                var blankSolutions = _solutions.AllDevelopSolutions.Select( x => x.GetSolution( m, true, _solutions.World.DevelopLocalBranchName ) );
                if( blankSolutions.Any( s => s == null ) ) return false;

                var deps = DependencyContext.Create( m, blankSolutions );
                if( deps == null ) return false;
                SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
                if( r.HasError ) r.RawSorterResult.LogError( m );
                else
                {
                    XGlobalCIBuildAction.DisplaySolutionList( m, r );

                    int startAt = 0;
                    Console.Write( "Start at:" );
                    while( !int.TryParse( Console.ReadLine(), out startAt ) ) ;

                    string blankStore = EnsureBlankStore( m );

                    var environmentVariables = new List<(string, string)>();
                    environmentVariables.Add( ("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", blankStore) );

                    foreach( var build in r.Solutions.Skip( startAt ) )
                    {
                        XGlobalCIBuildAction.DisplaySolutionList( m, r, build.Index );
                        using( m.OpenInfo( $"{build.Index} - {build.Solution}" ) )
                        {
                            if( !XGlobalCIBuildAction.UpgradeSolutionPackagesToTheMax( m, _localPackages, deps, build.Solution, true, true ) ) return false;

                            var path = _fileSystem.GetFileInfo( build.Solution.SolutionFolderPath ).PhysicalPath;
                            if( !XGlobalCIBuildAction.Run( m, path, "dotnet", "run --project CodeCakeBuilder -autointeraction", environmentVariables ) ) return false;
                            if( !XGlobalCIBuildAction.CopyReleasesPackagesToLocalFolder( m, _localPackages, build.Solution, true ) ) return false;
                        }
                    }
                }
            }
            return true;
        }

        string EnsureBlankStore( IActivityMonitor m )
        {
            string path = Path.Combine( _localPackages.EnsureLocalFeedBlankFolder( m ).PhysicalPath, "CKSetupStore" );
            if( !Directory.Exists( path ) ) Directory.CreateDirectory( path );
            using( var s = LocalStore.OpenOrCreate( m, path ) )
            {
                s.PrototypeStoreUrl = Facade.DefaultStoreUrl;
            }
            return path;
        }

    }


}
