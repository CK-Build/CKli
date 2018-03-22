using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XGlobalBuildAction : XAction
    {
        readonly XSolutionCentral _solutions;
        readonly FileSystem _fileSystem;

        public XGlobalBuildAction(
            Initializer intializer,
            FileSystem fileSystem,
            ActionCollector collector,
            XSolutionCentral solutions )
            : base( intializer, collector )
        {
            _fileSystem = fileSystem;
            _solutions = solutions;
        }

        public override bool Run( IActivityMonitor m )
        {
            var all = _solutions.LoadAllSolutions( m, false );
            if( all == null ) return false;
            var deps = DependencyContext.Create( m, all );
            if( deps == null ) return false;
            SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
            if( r.HasError ) r.RawSorterResult.LogError( m );
            else
            {
                foreach( var build in r.DependencyTable.Select( d => (d.Index, d.Solution) )
                                                         .Distinct()
                                                         .OrderBy( t => t.Index ) )
                {
                    using( m.OpenInfo( $"Building {build.Solution}" ) )
                    {
                        var path = _fileSystem.GetFileInfo( build.Solution.SolutionFolderPath ).PhysicalPath;
                        if( path == null )
                        {
                            m.Error( $"Unable to build {build.Solution}. It must be in a checked out branch." );
                            return false;
                        }
                        if( !Run( m, path, "dotnet", "run --project CodeCakeBuilder -target=Build -autointeraction" ) )
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }


        static bool Run( IActivityMonitor m,
                         string workingDir,
                         string fileName,
                         string arguments )
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
            cmdStartInfo.Arguments = arguments;
            using( m.OpenTrace( $"{fileName} {cmdStartInfo.Arguments}" ) )
            using( Process cmdProcess = new Process() )
            {
                StringBuilder conOut = new StringBuilder();
                cmdProcess.StartInfo = cmdStartInfo;
                cmdProcess.ErrorDataReceived += ( o, e ) => { if( !string.IsNullOrEmpty( e.Data ) ) conOut.Append( "<StdErr> " ).AppendLine( e.Data ); };
                cmdProcess.OutputDataReceived += ( o, e ) => { if( e.Data != null ) conOut.Append( "<StdOut> " ).AppendLine( e.Data ); };
                cmdProcess.Start();
                cmdProcess.BeginErrorReadLine();
                cmdProcess.BeginOutputReadLine();
                cmdProcess.WaitForExit();

                if( conOut.Length > 0 ) m.Info( conOut.ToString() );
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
