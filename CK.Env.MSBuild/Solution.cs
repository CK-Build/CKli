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
using System.Diagnostics;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// A MSBuild solution file that may be in a <see cref="GitFolder"/> or not.
    /// </summary>
    public sealed class Solution : IDependentItemContainerRef
    {
        SolutionSpecialType _specialType;
        List<Solution> _secondarySolutions;
        List<ProjectBase> _allProjects;
        bool _isDirty;

        /// <summary>
        /// Gets the <see cref="MSBuildContext"/> from which this solution has been loaded.
        /// </summary>
        public MSBuildContext BuildContext { get; }

        /// <summary>
        /// Gets the .sln path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath FilePath { get; }

        /// <summary>
        /// Gets the folder path (relative to the <see cref="FileSystem"/>).
        /// </summary>
        public NormalizedPath SolutionFolderPath { get; }

        /// <summary>
        /// Gets the <see cref="GitFolder"/> to which the solution belongs
        /// or null if the solution was not in a Git repository.
        /// </summary>
        public GitFolder GitFolder { get; }

        /// <summary>
        /// Gets a <see cref="IGitBranchPlugin"/> in the current <see cref="BranchName"/>.
        /// </summary>
        /// <typeparam name="T">Plugin type.</typeparam>
        /// <returns>The plugin or null.</returns>
        public T GetPlugin<T>() where T : class, IGitBranchPlugin
        {
            return BranchName != null
                    ? GitFolder.PluginManager.BranchPlugins[BranchName].GetPlugin<T>()
                    : null;
        }

        /// <summary>
        /// Gets the solution settings that applies to this solution.
        /// </summary>
        public ISolutionSettings Settings { get; }

        /// <summary>
        /// Gets the branch name.
        /// Null if <see cref="GitFolder"/> is null or if the solution
        /// is not in "branches" or "remotes".
        /// </summary>
        public string BranchName { get; }

        /// <summary>
        /// Gets whether any of the the projects this solution contains need to be saved.
        /// </summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// Raised whenever <see cref="IsDirty"/> has changed.
        /// </summary>
        public event EventHandler IsDirtyChanged;

        /// <summary>
        /// Saves all files that have been modified.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Save( IActivityMonitor m )
        {
            if( _isDirty )
            {
                foreach( var p in AllProjects )
                {
                    if( !p.ProjectFile.Save( m, BuildContext.FileSystem ) ) return false;
                }
                CheckDirty( false );
            }
            return true;
        }

        internal void CheckDirty( bool shouldBeDirty )
        {
            if( _isDirty != shouldBeDirty )
            {
                bool now = AllProjects.Any( p => p.ProjectFile.IsDirty );
                if( _isDirty != now )
                {
                    _isDirty = now;
                    IsDirtyChanged?.Invoke( this, EventArgs.Empty );
                }
            }
        }

        /// <summary>
        /// Gets the solution name. This should be unique accross any possible world.
        /// For primary solution, this is the folder name (same as the .sln file name without the .sln extension).
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
        /// Gets the primary solution if this is a secondary solution (null if this is a
        /// primary solution).
        /// </summary>
        public Solution PrimarySolution { get; private set; }

        /// <summary>
        /// Gets the special type for this secondary solution.
        /// </summary>
        public SolutionSpecialType SpecialType => _specialType;

        /// <summary>
        /// Gets all solution projects, including any <see cref="SolutionFolder"/>.
        /// </summary>
        public IReadOnlyCollection<ProjectBase> AllBaseProjects => _allProjects;

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
        /// They should be located in a "Tests" directory.
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
        /// Creates a new project that must not already exist.
        /// TODO: impact .sln (currently nothing is done).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="framework">The framework or frameworks from <see cref="MSBuildContext.Traits"/>. Must not be empty.</param>
        /// <param name="projectName">The project name. Must not be null or whitespace and must not already exist.</param>
        /// <param name="configure">Optional configurator.</param>
        /// <returns>The new project.</returns>
        public Project CreateNewClassLibraryProject( IActivityMonitor m, CKTrait framework, string projectName, Action<XDocument> configure = null )
        {
            if( String.IsNullOrWhiteSpace( projectName ) ) throw new ArgumentNullException( nameof( projectName ) );
            if( framework.IsEmpty || framework.Context != MSBuildContext.Traits ) throw new ArgumentException( "Invalid framework.", nameof( framework ) );
            var project = AllProjects.FirstOrDefault( p => StringComparer.OrdinalIgnoreCase.Equals( p.Name, projectName ) );
            if( project != null ) throw new InvalidOperationException( $"Project {projectName} already exists." );
            {
                var projectDirectory = SolutionFolderPath.AppendPart( projectName );
                var projectFilePath = projectDirectory.AppendPart( projectName + ".csproj" );
                var file = BuildContext.FileSystem.GetFileInfo( projectFilePath );
                if( !file.Exists )
                {
                    XDocument d = new XDocument(
                                    new XElement( "Project", new XAttribute( "Sdk", "Microsoft.NET.Sdk" ),
                                      new XElement( "PropertyGroup",
                                        new XElement( framework.IsAtomic ? "TargetFramework" : "TargetFrameworks", framework ) ) ) );
                    configure?.Invoke( d );
                    if(!BuildContext.FileSystem.EnsureDirectory( m, projectDirectory )
                        || !BuildContext.FileSystem.CopyTo( m, d.Beautify().ToString(), projectFilePath ) )
                    {
                        return null;
                    }
                }
                var projectGuid = Guid.NewGuid().ToString( "B" );
                project = new Project( BuildContext, projectGuid, projectName, projectFilePath, "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}" );
                project.Solution = this;
                if( project.ReloadProjectFile( m ) == null ) return null;
                _allProjects.Add( project );
            }
            return project;
        }


        /// <summary>
        /// Overridden to return the <see cref="BranchName"/>/<see cref="UniqueSolutionName"/>.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{BranchName}/{UniqueSolutionName}";

        string IDependentItemRef.FullName => UniqueSolutionName;

        bool IDependentItemRef.Optional => false;

        /// <summary>
        /// Gets the list of secondary solutions if any of them has been loaded.
        /// </summary>
        internal IEnumerable<Solution> LoadedSecondarySolutions => _secondarySolutions;

        /// <summary>
        /// Declares this solution as a secondary solution of an existing one.
        /// <see cref="PublishedProjects"/> is cleared when this method is called since a secondary solution
        /// is not aimed at producing packages.
        /// </summary>
        /// <param name="primarySolution">The required primary solution.</param>
        /// <param name="type">The solution type.</param>
        internal void SetAsSecondarySolution( Solution primarySolution, SolutionSpecialType type )
        {
            Debug.Assert( primarySolution != null && primarySolution.PrimarySolution == null );
            Debug.Assert( PrimarySolution == null );
            Debug.Assert( type != SolutionSpecialType.None );
            PrimarySolution = primarySolution;
            _specialType = type;
            PublishedProjects.Clear();
            if( primarySolution._secondarySolutions == null ) primarySolution._secondarySolutions = new List<Solution>();
            primarySolution._secondarySolutions.Add( this );
        }

        Solution(
            MSBuildContext buildContext,
            GitFolder gitFolder,
            string branchName,
            NormalizedPath filePath,
            NormalizedPath folderPath,
            ISolutionSettings settings,
            string version,
            string visualStudioVersion,
            string minimumVisualStudioVersion,
            List<ProjectBase> projects )
        {
            BuildContext = buildContext;
            GitFolder = gitFolder;
            BranchName = branchName;
            FilePath = filePath;
            SolutionFolderPath = folderPath;
            Settings = settings;
            Version = version;
            VisualStudioVersion = visualStudioVersion;
            MinimumVisualStudioVersion = minimumVisualStudioVersion;
            _allProjects = projects;
            foreach( var p in projects ) p.Solution = this;
            BuildProjects = projects.OfType<Project>().Where( p => p.Name == "CodeCakeBuilder" ).ToList();
            TestProjects = projects.OfType<Project>().Where( p => p.Name.EndsWith( ".Tests" ) ).ToList();
            PublishedProjects = projects.OfType<Project>().Where( p => p.Name != "CodeCakeBuilder"
                                                                        && !p.Name.EndsWith( ".Tests" )
                                                                        && p.Path.Parts.Count == FilePath.Parts.Count + 1 )
                                                          .ToList();
        }

        static internal Solution Load(
            IActivityMonitor m,
            MSBuildContext ctx,
            NormalizedPath filePath,
            ISolutionSettings settings )
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
                // Finds the GitFolder and then the branch name if inside a Git folder.
                var gitFolder = ctx.FileSystem.FindGitFolder( folderPath );
                string branchName = null;
                if( gitFolder != null )
                {
                    var p = folderPath.RemovePrefix( gitFolder.SubPath );
                    if( p.FirstPart == "branches" )
                    {
                        branchName = p.Parts[1];
                    }
                    else if( p.FirstPart == "remotes" )
                    {
                        // Skips the remote name.
                        branchName = p.Parts[2];
                    }
                    // Otherwise (like commit hash or other virtual directory), let the
                    // branch name be null.
                }
                var s = new Solution(
                    ctx,
                    gitFolder,
                    branchName,
                    filePath,
                    folderPath,
                    settings,
                    version,
                    visualStudioVersion,
                    minimumVisualStudioVersion,
                    projects );
                foreach( var p in s.AllProjects )
                {
                    if( p.ReloadProjectFile( m ) == null )
                    {
                        m.Error( $"Error while loading project '{p}'." );
                        return null;
                    }
                }
                return s;
            }
            catch( Exception ex )
            {
                m.Error( $"Error while parsing solution file '{filePath}'.", ex );
                return null;
            }
        }

        static ProjectBase ParseSolutionProjectLine( IActivityMonitor m, MSBuildContext ctx, NormalizedPath folderPath, string line )
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
            return new Project( ctx, projectGuid, projectName, path, type );
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
