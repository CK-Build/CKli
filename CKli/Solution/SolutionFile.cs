using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Solution
{
    /// <summary>
    /// Represents the content in an MSBuild solution file.
    /// </summary>
    public sealed class SolutionFile
    {
        /// <summary>
        /// Gets the file format version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the version of Visual Studio that created the file.
        /// </summary>
        public string VisualStudioVersion { get; }

        /// <summary>
        /// Gets the minimum supported version of Visual Studio.
        /// </summary>
        public string MinimumVisualStudioVersion { get; }

        /// <summary>
        /// Gets all solution projects.
        /// </summary>
        public IReadOnlyCollection<SolutionProject> Projects { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SolutionFile"/> class.
        /// </summary>
        /// <param name="version">The file format version.</param>
        /// <param name="visualStudioVersion">The version of Visual Studio that created the file.</param>
        /// <param name="minimumVisualStudioVersion">The minimum supported version of Visual Studio.</param>
        /// <param name="projects">The solution projects.</param>
        public SolutionFile(
            string version,
            string visualStudioVersion,
            string minimumVisualStudioVersion,
            IReadOnlyCollection<SolutionProject> projects )
        {
            Version = version;
            VisualStudioVersion = visualStudioVersion;
            MinimumVisualStudioVersion = minimumVisualStudioVersion;
            Projects = projects;
        }

        /// <summary>
        /// Parses a MSBuild solution.
        /// </summary>
        /// <param name="solutionPath">The solution path.</param>
        /// <returns>A parsed solution.</returns>
        static public SolutionFile Parse( IFileProvider fileSystem, NormalizedPath solutionPath )
        {
            if( fileSystem == null ) throw new ArgumentNullException( nameof( fileSystem ) );
            var file = fileSystem.GetFileInfo( solutionPath ).AsTextFileInfo();
            string version = null;
            string visualStudioVersion = null;
            string minimumVisualStudioVersion = null;
            var projects = new List<SolutionProject>();
            bool inNestedProjectsSection = false;
            foreach( var line in file.ReadAsTextLines() )
            {
                var trimmed = line.Trim();
                if( line.StartsWith( "Project(\"{" ) )
                {
                    var project = ParseSolutionProjectLine( solutionPath, line );
                    if( StringComparer.OrdinalIgnoreCase.Equals( project.Type, SolutionFolder.TypeIdentifier ) )
                    {
                        projects.Add( new SolutionFolder( project.Id, project.Name, project.Path ) );
                        continue;
                    }
                    projects.Add( project );
                }
                else if( line.StartsWith( "Microsoft Visual Studio Solution File, " ) )
                {
                    version = string.Concat( line.Skip( 39 ) );
                }
                else if( line.StartsWith( "VisualStudioVersion = " ) )
                {
                    visualStudioVersion = string.Concat( line.Skip( 22 ) );
                }
                else if( line.StartsWith( "MinimumVisualStudioVersion = " ) )
                {
                    minimumVisualStudioVersion = string.Concat( line.Skip( 29 ) );
                }
                else if( trimmed.StartsWith( "GlobalSection(NestedProjects)" ) )
                {
                    inNestedProjectsSection = true;
                }
                else if( inNestedProjectsSection && trimmed.StartsWith( "EndGlobalSection" ) )
                {
                    inNestedProjectsSection = false;
                }
                else if( inNestedProjectsSection )
                {
                    ParseNestedProjectLine( projects, trimmed );
                }
            }
            var solutionParserResult = new SolutionFile(
                version,
                visualStudioVersion,
                minimumVisualStudioVersion,
                projects.AsReadOnly() );
            return solutionParserResult;
        }

        static SolutionProject ParseSolutionProjectLine( NormalizedPath solutionPath, string line )
        {
            var withinQuotes = false;
            var projectTypeBuilder = new StringBuilder();
            var nameBuilder = new StringBuilder();
            var pathBuilder = new StringBuilder();
            var idBuilder = new StringBuilder();
            var result = new[]
            {
                projectTypeBuilder,
                nameBuilder,
                pathBuilder,
                idBuilder
            };
            var position = 0;
            foreach( var c in line.Skip( 8 ) )
            {
                if( c == '"' )
                {
                    withinQuotes = !withinQuotes;
                    if( !withinQuotes )
                    {
                        if( position++ >= result.Length )
                        {
                            break;
                        }
                    }
                    continue;
                }
                if( !withinQuotes )
                {
                    continue;
                }
                result[position].Append( c );
            }
            return new SolutionProject(
                idBuilder.ToString(),
                nameBuilder.ToString(),
                solutionPath.RemoveLastPart().Combine( pathBuilder.ToString() ),
                projectTypeBuilder.ToString() );
        }

        static void ParseNestedProjectLine( List<SolutionProject> projects, string line )
        {
            // pattern: {Child} = {Parent}
            var projectIds = line.Split( new[] { " = " }, StringSplitOptions.RemoveEmptyEntries );
            var child = projects.FirstOrDefault( x => StringComparer.OrdinalIgnoreCase.Equals( x.Id, projectIds[0].Trim() ) );
            if( child == null ) return;

            // Parent should be a folder
            var parent = projects.FirstOrDefault( x => StringComparer.OrdinalIgnoreCase.Equals( x.Id, projectIds[1].Trim() ) ) as SolutionFolder;
            if( parent == null ) return;

            parent.Items.Add( child );
            child.Parent = parent;
        }

    }
}
