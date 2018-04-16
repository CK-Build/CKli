using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Env.MSBuild;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli.MSBuild.Issues
{
    [CK.Env.XName( "TestProjectsStandardConventions" )]
    public class XTestProjectsStandardConventionsIssuer : XIssuer
    {
        readonly XSolutionBase _solution;

        public XTestProjectsStandardConventionsIssuer(
            XSolutionBase solution,
            IssueCollector collector,
            Initializer initializer )
            : base( collector, initializer )
        {
            _solution = solution;
        }

        protected override bool CreateIssue( IRunContextIssue builder )
        {
            var s = _solution.Solution;
            using( builder.Monitor.OpenInfo( $"Analysing Tests in solution '{s.UniqueSolutionName}'." ) )
            {
                var testsPath = s.SolutionFolderPath.AppendPart( "Tests" );
                // Checks projects first.
                bool hasTests = false;
                foreach( var p in s.AllProjects )
                {
                    hasTests |= p.IsTestProject;
                    bool isInTestsFolder = p.Path.StartsWith( testsPath );
                    if( p.IsTestProject && !isInTestsFolder )
                    {
                        builder.CreateIssue( $"TestProjectMustBeInTestsFolder:{p.Path}", $"Project {p.Name} should be in 'Tests' folder." );
                    }
                    if( p.IsTestProject && !p.Name.EndsWith( ".Tests" ) )
                    {
                        builder.CreateIssue( $"TestsNameSuffix:{p.Path}", $"Project {p.Name}: name should end with '.Tests' (or be marked with <IsTestProject>False</IsTestProject> if it is not a test project)." );
                    }
                    else if( !p.IsTestProject && p.Name.EndsWith( ".Tests" ) )
                    {
                        builder.CreateIssue( $"TestsNameSuffixOnNonTestProject:{p.Path}", $"Project {p.Name}: name should NOT end with '.Tests' since this is not a Test project (or be marked with <IsTestProject>True</IsTestProject> if it actually is a test project)." );
                    }
                    if( p.IsTestProject )
                    {
                        var settings = p.Path.RemoveLastPart().AppendPart( "Properties" ).AppendPart( "launchSettings.json" );
                        var fInfo = _solution.FileSystem.GetFileInfo( settings ).AsTextFileInfo();
                        if( fInfo != null )
                        {
                            JObject o = JObject.Parse( fInfo.TextContent );
                            var c = o.Descendants().OfType<JProperty>().Where( prop => prop.Name == "commandLineArgs"
                                                                               && prop.Value.Type == JTokenType.String
                                                                               && (((string)prop.Value).StartsWith( "bin\\Debug\\net461\\", StringComparison.OrdinalIgnoreCase )
                                                                                    || ((string)prop.Value).StartsWith( "bin\\Release\\net461\\", StringComparison.OrdinalIgnoreCase )) );
                            if( c.Any() )
                            {
                                builder.CreateIssue( $"RootOfLaunch:{settings}", $"Removes target path from commandLineArgs in launchsettings.json.", m =>
                                   {
                                       foreach( var prop in c )
                                       {
                                           prop.Value = ((string)prop.Value).Replace( "bin\\Debug\\net461\\", "" )
                                                                            .Replace( "bin\\Release\\net461\\", "" );
                                       }
                                       _solution.FileSystem.CopyTo( m, o.ToString(), settings );
                                       return true;
                                   } );
                            }
                        }

                    }
                }
                if( hasTests )
                {
                    var solutionFolder = s.AllBaseProjects.OfType<SolutionFolder>().FirstOrDefault( p => p.Name == "Tests" );
                    if( solutionFolder == null )
                    {
                        builder.CreateIssue( $"MissingTestsSolutionFolder:{s.UniqueSolutionName}", "Missing Solution Folder 'Tests'." );
                    }
                    else if( solutionFolder.Path != testsPath )
                    {
                        builder.CreateIssue( $"TestsFolder:{s.UniqueSolutionName}", $"Folder 'Tests' must be '{testsPath}'." );
                    }
                }
            }
            return true;
        }
    }
}
