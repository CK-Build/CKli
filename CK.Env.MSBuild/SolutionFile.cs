using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using CK.Core;
using System.IO;
using System.Xml.Linq;
using CK.Setup;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Represents the content in an MSBuild solution file.
    /// </summary>
    public sealed class SolutionFile : IDependentItemContainerRef
    {
        SolutionFileSpecialType _specialType;

        /// <summary>
        /// Gets the .sln path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath FilePath { get; }

        /// <summary>
        /// Gets the folder path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath SolutionFolderPath { get; }

        /// <summary>
        /// Gets the solution name. This should be unique accross any possible world.
        /// For primary solution, this is the folder name (the .sln file name without the .sln extension).
        /// For secondary solutions, this is the "name of the primary solution"/"solution file name" (including
        /// the .sln extension) in order for the secondary solution to be scoped by the primary name and, with
        /// the traling .sln to easily be spotted as a secondary solution.
        /// However, since this solution name does not contain the branch name, duplicates
        /// may occur if the same solution is loaded from different branches.
        /// </summary>
        public string UniqueSolutionName
        {
            get
            {
                return PrimarySolution == null
                        ? SolutionFolderPath.Parts[SolutionFolderPath.Parts.Count - 3]
                        : PrimarySolution.UniqueSolutionName + '/' + FilePath.LastPart;

            }
        }

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
        /// Gets or sets the primary solution if this is a secondary solution (null if this is a
        /// primary solution).
        /// When set, this acts as a Container in terms of dependencies when <see cref="SolutionSortStrategy.EverythingExceptBuildProjects"/>
        /// is used.
        /// </summary>
        public SolutionFile PrimarySolution { get; set; }

        /// <summary>
        /// Gets or sets a special type for this solution.
        /// </summary>
        public SolutionFileSpecialType SpecialType
        {
            get => _specialType;
            set
            {
                if( value != SolutionFileSpecialType.None && PrimarySolution == null )
                {
                    throw new ArgumentException( "Invalid SpecialType for a Primary solution." );
                }
                _specialType = value;
            }
        }

        /// <summary>
        /// Gets all solution projects, including any <see cref="SolutionFolder"/>.
        /// </summary>
        public IReadOnlyCollection<ProjectBase> AllBaseProjects { get; }

        /// <summary>
        /// Gets all projects.
        /// This includes <see cref="PublishedProjects"/>, <see cref="TestProjects"/>,
        /// <see cref="BuildProjects"/> and <see cref="MiscProjects"/>.
        /// </summary>
        public IEnumerable<Project> AllProjects => AllBaseProjects.OfType<Project>();

        /// <summary>
        /// Gets the published projects (the ones that will be packaged). These projetcs are by default
        /// all projects at the root of the solution for primary solutions. For secondary solutions
        /// this is empty by default.
        /// </summary>
        public IList<Project> PublishedProjects { get; }

        /// <summary>
        /// Gets the test projects. These projetcs are by default
        /// all projects whose name ends with ".Tests".
        /// They sould be located in a "Tests" directory.
        /// </summary>
        public IList<Project> TestProjects { get; }

        /// <summary>
        /// Gets the build projects. By default, this contains the "CodeCakeBuilder" project
        /// if it exists.
        /// </summary>
        public IList<Project> BuildProjects { get; }

        /// <summary>
        /// Gets the projects that are not in <see cref="PublishedProjects"/>, <see cref="TestProjects"/>,
        /// and <see cref="BuildProjects"/>.
        /// </summary>
        public IEnumerable<Project> MiscProjects => AllProjects
                                                        .Except( PublishedProjects )
                                                        .Except( TestProjects )
                                                        .Except( BuildProjects );

        /// <summary>
        /// Initializes all <see cref="Project.Deps"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>False if an error occurred. True on success.</returns>
        public bool InitializeProjectsDeps( IActivityMonitor m )
        {
            foreach( var p in AllProjects )
            {
                if( !p.InitializeDeps( m, false ).IsInitialized ) return false;
            }
            return true;
        }

        public override string ToString() => $"Solution '{UniqueSolutionName}'.";

        string IDependentItemRef.FullName => UniqueSolutionName;

        bool IDependentItemRef.Optional => false;

        SolutionFile(
            SolutionFile primarySolution,
            NormalizedPath filePath,
            NormalizedPath folderPath,
            string version,
            string visualStudioVersion,
            string minimumVisualStudioVersion,
            IReadOnlyCollection<ProjectBase> projects )
        {
            PrimarySolution = primarySolution;
            FilePath = filePath;
            SolutionFolderPath = folderPath;
            Version = version;
            VisualStudioVersion = visualStudioVersion;
            MinimumVisualStudioVersion = minimumVisualStudioVersion;
            AllBaseProjects = projects;
            foreach( var p in projects ) p.Solution = this;
            BuildProjects = projects.OfType<Project>().Where( p => p.Name == "CodeCakeBuilder" ).ToList();
            TestProjects = projects.OfType<Project>().Where( p => p.Name.EndsWith( ".Tests" ) ).ToList();
            if( primarySolution == null )
            {
                PublishedProjects = projects.OfType<Project>().Where( p => p.Name != "CodeCakeBuilder"
                                                                           && !p.Name.EndsWith( ".Tests" )
                                                                           && p.Path.Parts.Count == FilePath.Parts.Count + 1 )
                                                              .ToList();
            }
            else PublishedProjects = new List<Project>();
        }

        /// <summary>
        /// Parses a MSBuild solution.
        /// </summary>
        /// <param name="filePath">The solution path.</param>
        /// <param name="primarySolution">The primary solution if this is a secondary solution.</param>
        /// <returns>A parsed solution.</returns>
        static public SolutionFile Create( IActivityMonitor m, ProjectFileContext ctx, SolutionFile primarySolution, NormalizedPath filePath )
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
                    primarySolution,
                    filePath,
                    folderPath,
                    version,
                    visualStudioVersion,
                    minimumVisualStudioVersion,
                    projects.ToArray() );
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
