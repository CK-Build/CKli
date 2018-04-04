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

namespace CKli
{
    public class XGlobalBlankBuildAction : XAction
    {
        static readonly XNamespace SVGNS = XNamespace.Get( "http://csemver.org/schemas/2015" );
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
            var all = _solutions.AllSolutions.ToDictionary( s => s.Solution, s => s.GitBranch.Parent );
            var deps = DependencyContext.Create( m, all.Keys );
            if( deps == null ) return false;
            SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
            if( r.HasError ) r.RawSorterResult.LogError( m );
            else
            {
                using( m.OpenInfo( "Solutions to build:" ) )
                {
                    var list = r.DependencyTable.GroupBy( d => d.Index )
                                             .OrderBy( g => g.Key )
                                             .Select( g => (Index: g.Key, Rows: g.ToList()) )
                                             .Select( g => (
                                                            g.Index,
                                                            g.Rows[0].Solution,
                                                            HasAtLeastOneTargetProject: g.Rows[0].Target != null,
                                                            g.Rows) )
                                             .ToList();
                    int startAt = 0;
                    foreach( var build in list )
                    {
                        m.Info( $"{build.Index} - {build.Solution} {(build.HasAtLeastOneTargetProject ? "(has dependencies)" : "")}" );
                    }
                    Console.Write( "Start at:" );
                    while( !int.TryParse( Console.ReadLine(), out startAt ) ) ;

                    string blankStore = EnsureBlankStore( m );
                    var envVariables = new KeyValuePair<string, string>[]
                    {
                        new KeyValuePair<string, string>("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", blankStore)
                    };

                    foreach( var build in list.Skip( startAt ) )
                    {
                        using( m.OpenInfo( $"{build.Index} - {build.Solution}" ) )
                        {
                            var gitFolder = all[build.Solution].GitFolder;
                            var commitResult = gitFolder.Commit( m, "Blank Build from CK-Env." );
                            if( !commitResult.Success ) return false;

                            if( build.HasAtLeastOneTargetProject )
                            {
                                var toUpgrade = build.Rows.Select( d => (Row: d, Version: _localPackages.GetLocalLastVersion( m, d.Target.Name, true )) )
                                                          .ToList();
                                var missings = toUpgrade.Where( u => u.Version == null );
                                if( missings.Any() )
                                {
                                    m.Fatal( $"Packages not found locally: {missings.Select( u => u.Row.Target.Name ).Concatenate()}." );
                                    return false;
                                }
                                using( m.OpenInfo( $"Upgrading {toUpgrade.GroupBy( u => u.Row.Target ).Count()} locally available packages." ) )
                                {
                                    foreach( var u in toUpgrade )
                                    {
                                        u.Row.Origin.SetPackageReferenceVersion( m, u.Row.Origin.TargetFrameworks, u.Row.Target.Name, u.Version );
                                    }
                                    if( !build.Solution.Save( m, _fileSystem ) ) return false;
                                }
                            }
                            bool resetSuccess = false;
                            using( Util.CreateDisposableAction( () => resetSuccess = gitFolder.ResetHard( m ) ) )
                            {
                                if( !SetRepositoryXmlIgnoreDirtyFolders( m, build.Solution, true ) ) return false;
                                if( !AddLocalFeedToNuGetSources( m, build.Solution, true ) ) return false;
                                var path = _fileSystem.GetFileInfo( build.Solution.SolutionFolderPath ).PhysicalPath;
                                if( !Run( m, path, "dotnet", "run --project CodeCakeBuilder -autointeraction", envVariables ) ) return false;
                                if( !CopyReleasesPackagesToLocalFolder( m, build.Solution, true ) ) return false;
                            }
                            if( !resetSuccess ) return false;
                        }
                    }
                }
            }
            return true;
        }

        string EnsureBlankStore( IActivityMonitor m )
        {
            Facade.
        }

        bool CopyReleasesPackagesToLocalFolder( IActivityMonitor m, Solution solution, bool toBlankFolder )
        {
            using( m.OpenInfo( $"Copying released Packages to LocalFeed{(toBlankFolder ? "/Blanck" : "")}." ) )
            {
                try
                {
                    var targetFolder = toBlankFolder
                                        ? _localPackages.EnsureLocalFeedBlankFolder( m ).PhysicalPath
                                        : _localPackages.EnsureLocalFeedFolder( m ).PhysicalPath;
                    var releasesFolder = solution.SolutionFolderPath.Combine( "CodeCakeBuilder/Releases" );
                    var packages = Directory.GetFiles( _fileSystem.GetFileInfo( releasesFolder ).PhysicalPath, "*.nupkg" );
                    foreach( var p in packages )
                    {
                        var target = Path.Combine( targetFolder, Path.GetFileName( p ) );
                        File.Copy( p, target, true );
                    }
                    return true;
                }
                catch( Exception ex )
                {
                    m.Fatal( ex );
                    return false;
                }
            }
        }

        bool SetRepositoryXmlIgnoreDirtyFolders( IActivityMonitor m, Solution solution, bool withBlankBranchName )
        {
            var pathXml = solution.SolutionFolderPath.AppendPart( "RepositoryInfo.xml" );
            var rXml = _fileSystem.GetFileInfo( pathXml );
            if( !rXml.Exists || rXml.IsDirectory || rXml.PhysicalPath == null )
            {
                m.Fatal( $"RepositoryInfo.xml is required and must be physically available." );
                return false;
            }
            var xDoc = rXml.ReadAsXDocument();
            var e = xDoc.Root;
            var debug = e.Element( SVGNS + "Debug" );
            if( debug == null ) e.Add( debug = new XElement( SVGNS + "Debug" ) );
            if( (string)debug.Attribute( "IgnoreDirtyWorkingFolder" ) != "true" )
            {
                debug.SetAttributeValue( "IgnoreDirtyWorkingFolder", "true" );
            }
            if( withBlankBranchName )
            {
                var branch = e.Elements( SVGNS + "Branches" )
                                .Elements( SVGNS + "Branch" )
                                .Where( b => (string)b.Attribute( "Name" ) == solution.BranchName );
                if( !branch.Any() )
                {
                    m.Fatal( $"Element <Branches><Branch Name='{solution.BranchName}'/> in RepositoryInfo.xml is required." );
                    return false;
                }
                branch.First().SetAttributeValue( "VersionName", "blank" );
            }
            return _fileSystem.CopyTo( m, xDoc.ToString(), pathXml );
        }

        bool AddLocalFeedToNuGetSources( IActivityMonitor m, Solution solution, bool withBlank )
        {
            var pathXml = solution.SolutionFolderPath.AppendPart( "nuget.config" );
            var rXml = _fileSystem.GetFileInfo( pathXml );
            if( !rXml.Exists || rXml.IsDirectory || rXml.PhysicalPath == null )
            {
                m.Fatal( $"nuget.config is required and must be physically available." );
                return false;
            }
            var xDoc = rXml.ReadAsXDocument();
            var e = xDoc.Root;
            var packageSources = e.Elements( "packageSources" );
            if( !packageSources.Any() || packageSources.Count() > 1 )
            {
                m.Fatal( $"nuget.config must contain one and only one <packageSources> element." );
                return false;
            }
            packageSources.Single().Add( new XElement( "add",
                                                new XAttribute( "key", "Local Feed" ),
                                                new XAttribute( "value", _localPackages.EnsureLocalFeedFolder( m ).PhysicalPath ) ) );
            if( withBlank )
            {
                packageSources.Single().Add( new XElement( "add",
                                                    new XAttribute( "key", "Blank Feed" ),
                                                    new XAttribute( "value", _localPackages.EnsureLocalFeedBlankFolder( m ).PhysicalPath ) ) );
            }
            return _fileSystem.CopyTo( m, xDoc.ToString(), pathXml );
        }

        static bool Run( IActivityMonitor m,
                         string workingDir,
                         string fileName,
                         string arguments,
                         IEnumerable<KeyValuePair<string,string>> environmentVariables )
        {
            ProcessStartInfo cmdStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            if( environmentVariables != null )
            {
                foreach( var kv in environmentVariables ) cmdStartInfo.EnvironmentVariables.Add( kv.Key, kv.Value );
            }
            cmdStartInfo.Arguments = arguments;
            using( m.OpenTrace( $"{fileName} {cmdStartInfo.Arguments}" ) )
            using( Process cmdProcess = new Process() )
            {
                cmdProcess.StartInfo = cmdStartInfo;
                cmdProcess.ErrorDataReceived += ( o, e ) => { if( !string.IsNullOrEmpty( e.Data ) ) m.Info( "<StdErr> " + e.Data ); };
                cmdProcess.OutputDataReceived += ( o, e ) => { if( e.Data != null ) m.Info( "<StdOut> " + e.Data ); };
                cmdProcess.Start();
                cmdProcess.BeginErrorReadLine();
                cmdProcess.BeginOutputReadLine();
                cmdProcess.WaitForExit();

                if( cmdProcess.ExitCode != 0 )
                {
                    m.Error( $"Process returned ExitCode {cmdProcess.ExitCode}." );
                    return false;
                }
                return true;
            }
        }
    }


}
