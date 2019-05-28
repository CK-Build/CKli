using CK.Text;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Project belongs to a <see cref="Solution"/>. It defines its <see cref="GeneratedArtifacts"/>, <see cref="PackageReferences"/>,
    /// <see cref="ProjectReferences"/> and whether it is a <see cref="IsTestProject"/>, <see cref="IsPublished"/> or whether it is
    /// the <see cref="Solution.BuildProject"/> or not.
    /// </summary>
    public class Project : TaggedObject, IProject
    {
        readonly List<GeneratedArtifact> _generatedArtifacts;
        readonly List<ProjectReference> _projectReferences;
        readonly List<PackageReference> _packageReferences;
        readonly List<NormalizedPath> _projectSources;
        Solution _solution;
        string _name;
        bool _isPublished;
        bool _isTestProject;

        internal Project(
            Solution s,
            NormalizedPath primarySolutionRelativeFolderPath,
            NormalizedPath fullFolderPath,
            string type,
            string simpleProjecName )
        {
            _solution = s;
            Type = type;
            SolutionRelativeFolderPath = primarySolutionRelativeFolderPath;
            FullFolderPath = fullFolderPath;
            SimpleProjectName = simpleProjecName;
            _generatedArtifacts = new List<GeneratedArtifact>();
            _projectReferences = new List<ProjectReference>();
            _packageReferences = new List<PackageReference>();
            _projectSources = new List<NormalizedPath>();

            _projectSources.Add( SolutionRelativeFolderPath );
        }

        internal void NormalizeName( IReadOnlyList<Project> homonyms )
        {
            Debug.Assert( homonyms.Contains( this ) );
            var sameSolutions = homonyms.Where( h => h != this && h._solution == _solution ).ToList();

            // If another project with the same SimpleName appear in (at least) another solutions we
            // need to use the Solution name.
            bool needSolutionName = homonyms.Count != sameSolutions.Count + 1;
            // Homonyms in the same solution need to be differentiated.
            bool needTypeOrPath = sameSolutions.Count > 0;

            bool typeIsUseless = !needTypeOrPath || sameSolutions.All( h => h.Type == Type );
            bool typeIsEnough = needTypeOrPath && !typeIsUseless && sameSolutions.All( h => h.Type != Type );
            bool needType = typeIsEnough || (needTypeOrPath && !typeIsUseless && sameSolutions.Any( h => h.Type != Type ));

            bool needPath = needTypeOrPath && !typeIsEnough;

            string name;
            if( needPath )
            {
                if( SolutionRelativeFolderPath.LastPart == SimpleProjectName )
                {
                    name = SolutionRelativeFolderPath.Path;
                }
                else
                {
                    name = SolutionRelativeFolderPath.Path + '/' + SimpleProjectName;
                }
            }
            else name = SimpleProjectName;
            if( needType ) name = '(' + Type + ')' + name;
            if( needSolutionName ) name = _solution.Name + '|' + name;

            _name = name;
        }

        internal void Detach()
        {
            Debug.Assert( _solution != null );
            _solution = null;
        }

        internal void CheckSolution()
        {
            if( _solution == null ) throw new InvalidOperationException( $"Project '{_name}' has been removed from its Solution." );
        }

        void CheckNotBuildProject( string something )
        {
            if( _solution.BuildProject == this ) throw new InvalidOperationException( $"Project {ToString()} cannot {something} since it's the Solution's BuildProject." );
        }

        /// <summary>
        /// Gets the solution to which this project belongs.
        /// This is null if this project has been removed from its Solution.
        /// </summary>
        public Solution Solution => _solution;

        ISolution IProject.Solution => _solution;

        /// <summary>
        /// Gets the path to this project relative to the <see cref="Solution"/>.
        /// </summary>
        public NormalizedPath SolutionRelativeFolderPath { get; }

        /// <summary>
        /// Gets the full path to this project folder that starts wiith the <see cref="Solution.FullPath"/>.
        /// </summary>
        public NormalizedPath FullFolderPath { get; }

        /// <summary>
        /// Gets the simple project name that may be ambiguous: see <see cref="Name"/>.
        /// </summary>
        public string SimpleProjectName { get; }

        /// <summary>
        /// Gets the project name: it can be the <see cref="SimpleProjectName"/>, prefixed or
        /// not by the <see cref="Solution.Name"/> and automatically prefixed by (<see cref="Type"/>)
        /// or the specified with <see cref="SolutionRelativeFolderPath"/> in case of conflicts
        /// with other projects in the <see cref="SolutionContext"/>.
        /// Given that the solution name is unique by design and that only one project of a given type can
        /// be defined in a folder, this name is eventually always unique. 
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Gets whether this project is published: this project can be "installed" in
        /// dependent solutions thanks to at least one <see cref="GeneratedArtifacts"/>
        /// that is <see cref="ArtifactType.IsInstallable"/>.
        /// </summary>
        public bool IsPublished
        {
            get => _isPublished;
            private set 
            {
                if( _isPublished != value )
                {
                    if( value ) CheckNotBuildProject( "be published" );
                    _isPublished = value;
                    _solution.OnIsPublishedChange( this );
                }
            }
        }

        /// <summary>
        /// Gets or sets whether this project is a test project.
        /// </summary>
        public bool IsTestProject
        {
            get => _isTestProject;
            set
            {
                CheckSolution();
                if( _isTestProject != value )
                {
                    if( value ) CheckNotBuildProject( "be a Test project" );
                    _isTestProject = value;
                    _solution.OnIsTestProjectChanged( this );
                }
            }
        }

        /// <summary>
        /// Gets or sets whether this project is the <see cref="Solution.BuildProject"/>.
        /// </summary>
        public bool IsBuildProject
        {
            get => Solution.BuildProject == this;
            set
            {
                CheckSolution();
                if( value ) _solution.BuildProject = this;
                else if( _solution.BuildProject == this ) _solution.BuildProject = this;
            }
        }

        /// <summary>
        /// Gets all artifacts that this project generates. 
        /// </summary>
        public IReadOnlyList<GeneratedArtifact> GeneratedArtifacts => _generatedArtifacts;

        /// <summary>
        /// Adds a new generated artifact.
        /// Throws a <see cref="InvalidOperationException"/> if the artifact already appears in generated artifatcs
        /// of all the projetcs of this <see cref="Solution"/> (same <see cref="Artifact.TypedName"/>).
        /// </summary>
        /// <param name="a">The artifact to add.</param>
        public void AddGeneratedArtifacts( Artifact a )
        {
            CheckSolution();
            _solution.CheckNewArtifact( a );
            _generatedArtifacts.Add( new GeneratedArtifact( a, this ) );
            Solution.OnArtifactAdded( a, this );
            IsPublished |= a.Type.IsInstallable;
        }

        /// <summary>
        /// Removes an artifact from <see cref="GeneratedArtifact"/>.
        /// </summary>
        /// <param name="a">The artifact to remove.</param>
        /// <returns>True on succes, false if the artifact doen't exist.</returns>
        public bool RemoveGeneratedArtifact( Artifact a )
        {
            CheckSolution();
            int idx = _generatedArtifacts.IndexOf( g => g.Artifact == a );
            if( idx >= 0 )
            {
                _generatedArtifacts.RemoveAt( idx );
                _solution.OnArtifactRemoved( a, this );
                IsPublished = _generatedArtifacts.Any( g => g.Artifact.Type.IsInstallable );
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the project type.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets a set of full paths (folder or files) that are "sources" for this project.
        /// By default, <see cref="FullFolderPath"/> is systematically added to this set.
        /// Any file and or folder that are outside the project folder should be added to this
        /// set (typically files or folders shared accross multiple projects).
        /// This is used as the default for <see cref="GeneratedArtifact.ArtifactSources"/>
        /// in <see cref="GeneratedArtifacts"/>.
        /// </summary>
        public IReadOnlyCollection<NormalizedPath> ProjectSources => _projectSources;



        /// <summary>
        /// Gets the references to local projects.
        /// </summary>
        public IReadOnlyList<ProjectReference> ProjectReferences => _projectReferences;

        /// <summary>
        /// Adds a new <see cref="ProjectReference"/> to this project.
        /// </summary>
        /// <param name="target">The referenced project.</param>
        /// <param name="kind">Optional non standard dependency kind if needed.</param>
        public void AddProjectReference( Project target, ProjectDependencyKind kind = ProjectDependencyKind.Transitive )
        {
            CheckSolution();
            if( _projectReferences.Any( p => p.Target == target ) ) throw new InvalidOperationException( $"Project '{target}' is already referenced by '{ToString()}'." );
            if( target._solution != _solution ) throw new InvalidOperationException( $"Project '{target}' belongs to '{target._solution}' whereas {ToString()} belons to '{_solution}'." );
            DoAddProjectReference( target, kind );
        }

        /// <summary>
        /// Ensures that a new <see cref="ProjectReference"/> with the exact same target
        /// project and kind exists.
        /// </summary>
        /// <param name="target">The referenced project.</param>
        /// <param name="kind">The dependency kind.</param>
        public void EnsureProjectReference( Project target, ProjectDependencyKind kind = ProjectDependencyKind.Transitive )
        {
            CheckSolution();
            int idx = _projectReferences.IndexOf( r => r.Target == target );
            if( idx >= 0 )
            {
                var exists = _projectReferences[idx];
                if( exists.Kind == kind ) return;
                DoRemoveProjectReferenceAt( idx );
            }
            DoAddProjectReference( target, kind );
        }

        /// <summary>
        /// Removes a project reference.
        /// </summary>
        /// <param name="target">The target project.</param>
        /// <returns>True on success, false if the project is not referenced.</returns>
        public bool RemoveProjectReference( IProject target )
        {
            CheckSolution();
            int idx = _projectReferences.IndexOf( r => r.Target == target );
            if( idx >= 0 )
            {
                DoRemoveProjectReferenceAt( idx );
                return true;
            }
            return false;
        }

        void DoAddProjectReference( Project target, ProjectDependencyKind kind )
        {
            var r = new ProjectReference( this, target, kind );
            _projectReferences.Add( r );
            _solution.OnProjectReferenceAdded( r );
        }

        void DoRemoveProjectReferenceAt( int idx )
        {
            var r = _projectReferences[idx];
            _projectReferences.RemoveAt( idx );
            _solution.OnProjectReferenceRemoved( r );
        }

        /// <summary>
        /// Gets the package references.
        /// </summary>
        public IReadOnlyList<PackageReference> PackageReferences => _packageReferences;

        /// <summary>
        /// Adds a new <see cref="PackageReference"/> to this project.
        /// </summary>
        /// <param name="target">The referenced package.</param>
        /// <param name="kind">The dependency kind.</param>
        public void AddPackageReference( ArtifactInstance target, ProjectDependencyKind kind )
        {
            CheckSolution();
            if( _packageReferences.Any( p => p.Target.Artifact == target.Artifact ) ) throw new InvalidOperationException( $"Package '{target.Artifact}' is already referenced by '{ToString()}'." );
            DoAddPackageReference( target, kind );
        }

        /// <summary>
        /// Ensures that a new <see cref="PackageReference"/> with the exact same target and kind exists.
        /// </summary>
        /// <param name="target">The referenced package.</param>
        /// <param name="kind">The dependency kind.</param>
        public void EnsurePackageReference( ArtifactInstance target, ProjectDependencyKind kind )
        {
            CheckSolution();
            int idx = _packageReferences.IndexOf( p => p.Target.Artifact == target.Artifact );
            if( idx >= 0 )
            {
                var exists = _packageReferences[idx];
                if( exists.Kind == kind && exists.Target == target ) return;
                DoRemovePackageReferenceAt( idx );
            }
            DoAddPackageReference( target, kind );
        }

        /// <summary>
        /// Removes a package reference.
        /// </summary>
        /// <param name="target">The target installable artifact to remove.</param>
        /// <returns>True on success, false if the artifact is not referenced.</returns>
        public bool RemovePackageReference( Artifact target )
        {
            CheckSolution();
            int idx = _packageReferences.IndexOf( p => p.Target.Artifact == target );
            if( idx >= 0 ) DoRemovePackageReferenceAt( idx );
            return false;
        }

        void DoAddPackageReference( ArtifactInstance target, ProjectDependencyKind kind )
        {
            var r = new PackageReference( this, target, kind );
            _packageReferences.Add( r );
            _solution.OnPackageReferenceAdded( r );
        }

        void DoRemovePackageReferenceAt( int idx )
        {
            var r = _packageReferences[idx];
            _packageReferences.RemoveAt( idx );
            _solution.OnPackageReferenceRemoved( r );
        }

        /// <summary>
        /// Overridden to return the <see cref="Name"/>.
        /// </summary>
        /// <returns>The project's name.</returns>
        public override string ToString() => Name;

    }

}

