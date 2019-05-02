using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CK.Env
{
    public class DependentProject
    {
        readonly List<GeneratedArtifact> _generatedArtifacts;
        readonly List<ProjectReference> _projectReferences;
        readonly List<PackageReference> _packageReferences;
        string _name;
        bool _isPublished;
        bool _isFolderNameRequired;
        bool _isTypedNameRequired;
        bool _isTestProject;

        internal DependentProject(
            PrimarySolution s,
            NormalizedPath primarySolutionRelativeFolderPath,
            NormalizedPath fullFolderPath,
            string type,
            string simpleProjecName,
            bool folderNameRequired,
            bool typedNameRequired )
        {
            Solution = s;
            Type = type;
            PrimarySolutionRelativeFolderPath = primarySolutionRelativeFolderPath;
            FullFolderPath = fullFolderPath;
            SimpleProjectName = simpleProjecName;
            _isFolderNameRequired = folderNameRequired;
            _isTypedNameRequired = typedNameRequired;
            UpdateName();
            _generatedArtifacts = new List<GeneratedArtifact>();
            _projectReferences = new List<ProjectReference>();
            _packageReferences = new List<PackageReference>();
        }

        internal void OnProjectAdding( string simpleName, string type, ref bool folderNameRequired, ref bool typedNameRequired )
        {
            if( SimpleProjectName == simpleName )
            {
                if( Type == type )
                {
                    _isFolderNameRequired = folderNameRequired = true;
                }
                else
                {
                    _isTypedNameRequired = typedNameRequired = true;
                }
                UpdateName();
            }
        }

        void UpdateName()
        {
            _name = Solution.Name + '/' + (_isFolderNameRequired ? PrimarySolutionRelativeFolderPath.Path : SimpleProjectName);
            if( _isTypedNameRequired ) _name = Type + ':' + _name;
        }

        /// <summary>
        /// Gets the solution.
        /// </summary>
        public PrimarySolution Solution { get; }

        /// <summary>
        /// Gets the path to this project relative to the <see cref="Solution"/>.
        /// </summary>
        public NormalizedPath PrimarySolutionRelativeFolderPath { get; }

        /// <summary>
        /// Gets the full path to this project folder.
        /// </summary>
        public NormalizedPath FullFolderPath { get; }

        /// <summary>
        /// Gets the simple project name that may be ambiguous: see <see cref="Name"/>.
        /// </summary>
        public string SimpleProjectName { get; }

        /// <summary>
        /// Gets the project name: it is the <see cref="PrimarySolution.Name"/>/<see cref="SimpleProjectName"/>
        /// if possible but is automatically prefixed by (<see cref="Type"/>) or the simple name
        /// is replaced with <see cref="PrimarySolutionRelativeFolderPath"/> in case of conflicts
        /// with other projects in the solution.
        /// Given that the solution name is unique by design and that only one project of a given type can
        /// be defined in a folder, this name is eventually always unique. 
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Gets whether this project is published: this project can be "installed" in
        /// dependent solutions thanks to at least one <see cref="GeneratedArtifacts"/>
        /// that is <see cref="ArtifactType.IsInstallable"/>.
        /// </summary>
        public bool IsPublished => _isPublished;

        /// <summary>
        /// Gets whether this project is published: this project can be "installed" in
        /// dependent solutions thanks to at least one <see cref="GeneratedArtifacts"/>
        /// that is <see cref="ArtifactType.IsInstallable"/>.
        /// </summary>
        public bool IsTestProject
        {
            get => _isTestProject;
            set
            {
                if( _isTestProject != value )
                {
                    if( value ) CheckNotBuildProject( "ba a Test project" );
                    _isTestProject = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets whether this project is the <see cref="PrimarySolution.BuildProject"/>.
        /// </summary>
        public bool IsBuildProject
        {
            get => Solution.BuildProject == this;
            set
            {
                if( value ) Solution.BuildProject = this;
                else if( Solution.BuildProject == this ) Solution.BuildProject = this;
            }
        }

        void CheckNotBuildProject( string something )
        {
            if( Solution.BuildProject == this ) throw new InvalidOperationException( $"Project {ToString()} cannot {something} since it's the Solution's BuildProject." );
        }

        /// <summary>
        /// Gets all artifacts that this project generates. 
        /// </summary>
        public IReadOnlyList<GeneratedArtifact> GeneratedArtifacts => _generatedArtifacts;

        /// <summary>
        /// Adds a new generated artifact.
        /// </summary>
        /// <param name="a">The artifact to add.</param>
        public void AddGeneratedArtifacts( Artifact a )
        {
            Solution.CheckNewArtifact( a );
            _generatedArtifacts.Add( new GeneratedArtifact( a, this ) );
            _isPublished |= a.Type.IsInstallable;
            if( _isPublished ) CheckNotBuildProject( "be published" );
        }

        /// <summary>
        /// Gets the project type.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the references to local projects.
        /// </summary>
        public IReadOnlyList<ProjectReference> ProjectReferences => _projectReferences;

        /// <summary>
        /// Adds a new <see cref="ProjectReference"/> to this project.
        /// </summary>
        /// <param name="target">The referenced project.</param>
        /// <param name="kind">Optional non standard dependency kind if needed.</param>
        public void AddProjectReference( DependentProject target, ProjectDependencyKind kind = ProjectDependencyKind.Transitive )
        {
            if( _projectReferences.Any( p => p.Target == target ) ) throw new InvalidOperationException( $"Project '{target}' is already referenced by '{ToString()}'." );
            if( target.Solution != Solution ) throw new InvalidOperationException( $"Project '{target}' belongs to '{target.Solution}' whereas {ToString()} belons to '{Solution}'." );
            _projectReferences.Add( new ProjectReference( this, target, kind ) );
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
            if( _packageReferences.Any( p => p.Target.Artifact == target.Artifact ) ) throw new InvalidOperationException( $"Package '{target.Artifact}' is already referenced by '{ToString()}'." );
            _packageReferences.Add( new PackageReference( this, target, kind ) );
        }

    }

}

