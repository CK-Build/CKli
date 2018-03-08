using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using CK.Core;
using System.IO;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Represents the content in an MSBuild solution file.
    /// </summary>
    public sealed class SolutionFile
    {
        /// <summary>
        /// Gets the .sln path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath FilePath { get; }

        /// <summary>
        /// Gets the folder path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath SolutionFolderPath { get; }

        /// <summary>
        /// Gets the file system from which this solution has been loaded.
        /// </summary>
        public IFileProvider FileSystem { get; }

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
        public IReadOnlyCollection<ProjectBase> Projects { get; }

        SolutionFile(
            NormalizedPath filePath,
            NormalizedPath folderPath,
            string version,
            string visualStudioVersion,
            string minimumVisualStudioVersion,
            IReadOnlyCollection<ProjectBase> projects )
        {
            FilePath = filePath;
            SolutionFolderPath = folderPath;
            Version = version;
            VisualStudioVersion = visualStudioVersion;
            MinimumVisualStudioVersion = minimumVisualStudioVersion;
            Projects = projects;
        }

        /// <summary>
        /// Parses a MSBuild solution.
        /// </summary>
        /// <param name="filePath">The solution path.</param>
        /// <returns>A parsed solution.</returns>
        static public SolutionFile Create( IActivityMonitor m, ProjectFileContext ctx, NormalizedPath filePath )
        {
            if( ctx == null ) throw new ArgumentNullException( nameof( ctx ) );
            var file = ctx.FileSystem.GetFileInfo( filePath ).AsTextFileInfo();
            if( file == null )
            {
                m.Error( $"Unable to read solution file '{filePath}'." );
                return null;
            }
            try
            {
                NormalizedPath folderPath = filePath.RemoveLastPart();
                string version = null;
                string visualStudioVersion = null;
                string minimumVisualStudioVersion = null;
                var projects = new List<ProjectBase>();
                bool inNestedProjectsSection = false;
                foreach( var line in file.ReadAsTextLines() )
                {
                    var trimmed = line.Trim();
                    if( line.StartsWith( "Project(\"{" ) )
                    {
                        projects.Add( ParseSolutionProjectLine( m, ctx, folderPath, line ) );
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
                    filePath,
                    folderPath,
                    version,
                    visualStudioVersion,
                    minimumVisualStudioVersion,
                    projects.AsReadOnly() );
                return solutionParserResult;
            }
            catch( Exception ex )
            {
                m.Error( $"Error while parsing solution file '{filePath}'.", ex );
                return null;
            }
        }

        static ProjectBase ParseSolutionProjectLine( IActivityMonitor m, ProjectFileContext ctx, NormalizedPath folderPath, string line )
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
            string projectGuid = idBuilder.ToString();
            string projectName = nameBuilder.ToString();
            NormalizedPath path = folderPath.Combine( pathBuilder.ToString() );
            string type = projectTypeBuilder.ToString();
            if( type.Equals( SolutionFolder.TypeIdentifier, StringComparison.OrdinalIgnoreCase ) )
            {
                return new SolutionFolder( idBuilder.ToString(), nameBuilder.ToString(), path );
            }
            var p = new Project( projectGuid, projectName, path, type, ctx );
            p.LoadProjectFile( m );
            return p;
        }

        static void ParseNestedProjectLine( List<ProjectBase> projects, string line )
        {
            // pattern: {Child} = {Parent}
            var projectIds = line.Split( new[] { " = " }, StringSplitOptions.RemoveEmptyEntries );
            var child = projects.FirstOrDefault( x => StringComparer.OrdinalIgnoreCase.Equals( x.ProjectGuid, projectIds[0].Trim() ) );
            if( child != null )
            {
                // Parent should be a folder
                SolutionFolder parent = projects.FirstOrDefault( x => StringComparer.OrdinalIgnoreCase.Equals( x.ProjectGuid, projectIds[1].Trim() ) ) as SolutionFolder;
                if( parent != null )
                {
                    parent.Items.Add( child );
                    child.Parent = parent;
                }
            }
        }

    }
}
