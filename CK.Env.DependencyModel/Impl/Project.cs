using CK.Core;
using CK.Build;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Diagnostics.CodeAnalysis;

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
        readonly List<ProjectPackageReference> _packageReferences;
        readonly List<NormalizedPath> _projectSources;
        Solution? _solution;
        string _name;
        CKTrait? _savors;
        bool _isPublished;
        bool _isTestProject;

        internal Project( Solution s,
                          NormalizedPath primarySolutionRelativeFolderPath,
                          NormalizedPath fullFolderPath,
                          string type,
                          string simpleProjecName,
                          CKTrait? savors )
        {
            Debug.Assert( savors == null || !savors.IsEmpty );
            _solution = s;
            Type = type;
            SolutionRelativeFolderPath = primarySolutionRelativeFolderPath;
            FullFolderPath = fullFolderPath;
            SimpleProjectName = simpleProjecName;
            // _name will be computed by NormalizeName below.
            _name = String.Empty;
            _savors = savors;
            _generatedArtifacts = new List<GeneratedArtifact>();
            _projectReferences = new List<ProjectReference>();
            _packageReferences = new List<ProjectPackageReference>();
            _projectSources = new List<NormalizedPath>{ SolutionRelativeFolderPath };
        }

        internal void NormalizeName( IReadOnlyList<Project> homonyms )
        {
            Debug.Assert( homonyms.Contains( this ) && _solution != null );
            var sameSolutions = homonyms.Where( h => h != this && h._solution == _solution ).ToList();

            // If another project with the same SimpleName appear in (at least) another solution we
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

        [MemberNotNull(nameof(_solution))]
        internal void CheckSolution()
        {
            Throw.CheckState( $"Project '{_name}' has been removed from its Solution.", _solution != null );
        }

        void CheckNotBuildProject( string something )
        {
            Throw.CheckState( $"Project {ToString()} cannot {something} since it's the Solution's BuildProject.", _solution?.BuildProject != this );
        }

        /// <summary>
        /// Gets the solution to which this project belongs.
        /// This is null if this project has been removed from its Solution.
        /// </summary>
        public Solution? Solution => _solution;

        ISolution? IProject.Solution => _solution;
        ISolution IPackageReferrer.Solution => _solution;

        /// <summary>
        /// Gets the path to this project relative to the <see cref="Solution"/>.
        /// </summary>
        public NormalizedPath SolutionRelativeFolderPath { get; }

        /// <summary>
        /// Gets the full path to this project folder that starts with the <see cref="Solution.FullPath"/>.
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
                    _solution?.OnIsPublishedChange( this );
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
                    _solution?.OnIsTestProjectChanged( this );
                }
            }
        }

        /// <summary>
        /// Gets or sets whether this project is the <see cref="Solution.BuildProject"/>.
        /// </summary>
        public bool IsBuildProject
        {
            get => Solution?.BuildProject == this;
            set
            {
                CheckSolution();
                if( value ) _solution.BuildProject = this;
                else if( _solution.BuildProject == this ) _solution.BuildProject = this;
            }
        }

        /// <summary>
        /// Gets the savors of this project. This is null by default.
        /// <para>
        /// When not null, the <see cref="CKTrait.Context"/> can be any context (typically
        /// the <see cref="ArtifactType.ContextSavors"/> of the "primary" artifact produced by this project).
        /// </para>
        /// </summary>
        public CKTrait? Savors => _savors;

        /// <summary>
        /// Applies a transformation to all the savors in this project: this <see cref="Savors"/>
        /// first and then all <see cref="ProjectPackageReference.ApplicableSavors"/> and <see cref="ProjectReference.ApplicableSavors"/>.
        /// </summary>
        /// <param name="f"></param>
        public void TransformSavors( Func<CKTrait?,CKTrait?> f )
        {
            _savors = f( _savors );
            for( int i = 0; i < _packageReferences.Count; ++i )
            {
                _packageReferences[i] = new ProjectPackageReference( _packageReferences[i], f );
            }
            for( int i = 0; i < _projectReferences.Count; ++i )
            {
                _projectReferences[i] = new ProjectReference( _projectReferences[i], f );
            }
            Solution?.OnProjectSavorsTransformed( this );
        }

        /// <summary>
        /// Gets all artifacts that this project generates. 
        /// </summary>
        public IReadOnlyList<GeneratedArtifact> GeneratedArtifacts => _generatedArtifacts;

        /// <summary>
        /// Adds a new generated artifact.
        /// Throws a <see cref="InvalidOperationException"/> if the artifact already appears in generated artifacts
        /// of all the projects of this <see cref="Solution"/> (same <see cref="Artifact.TypedName"/>).
        /// </summary>
        /// <param name="a">The artifact to add.</param>
        public void AddGeneratedArtifacts( Artifact a )
        {
            Throw.CheckArgument( a.IsValid );
            CheckSolution();
            _solution.CheckNewArtifact( a );
            _generatedArtifacts.Add( new GeneratedArtifact( a, this ) );
            _solution.OnArtifactAdded( a, this );
            IsPublished |= a.Type.IsInstallable;
        }

        /// <summary>
        /// Removes an artifact from <see cref="GeneratedArtifact"/>.
        /// </summary>
        /// <param name="a">The artifact to remove.</param>
        /// <returns>True on success, false if the artifact doesn't exist.</returns>
        public bool RemoveGeneratedArtifact( Artifact a )
        {
            Throw.CheckArgument( a.IsValid );
            CheckSolution();
            int idx = _generatedArtifacts.IndexOf( g => g.Artifact == a );
            if( idx >= 0 )
            {
                _generatedArtifacts.RemoveAt( idx );
                _solution.OnArtifactRemoved( a, this );
                IsPublished = _generatedArtifacts.Any( g => g.Artifact.Type!.IsInstallable );
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
        /// set (typically files or folders shared across multiple projects).
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
        /// <param name="applicableSavors">Optional savors that, when defined, must be a subset of this <see cref="Savors"/>.</param>
        public void AddProjectReference( Project target, ArtifactDependencyKind kind = ArtifactDependencyKind.Transitive, CKTrait? applicableSavors = null )
        {
            CheckSolution();
            Throw.CheckState( $"Project '{target}' is already referenced by '{ToString()}'.", !_projectReferences.Any( p => p.Target == target ) );
            Throw.CheckState( $"Project '{target}' belongs to '{target._solution}' whereas {ToString()} belongs to '{_solution}'.", target._solution != _solution );
            DoAddProjectReference( target, kind, applicableSavors ?? Savors );
        }

        /// <summary>
        /// Ensures that a new <see cref="ProjectReference"/> with the exact same target
        /// project and kind exists.
        /// </summary>
        /// <param name="target">The referenced project.</param>
        /// <param name="kind">The dependency kind.</param>
        /// <param name="applicableSavors">Optional savors that, when defined, must be a subset of this <see cref="Savors"/>.</param>
        public void EnsureProjectReference( Project target, ArtifactDependencyKind kind = ArtifactDependencyKind.Transitive, CKTrait? applicableSavors = null )
        {
            CheckSolution();
            var savors = applicableSavors ?? Savors;
            int idx = _projectReferences.IndexOf( r => r.Target == target );
            if( idx >= 0 )
            {
                var exists = _projectReferences[idx];
                if( exists.Kind == kind && exists.ApplicableSavors == savors ) return;
                DoRemoveProjectReferenceAt( idx );
            }
            DoAddProjectReference( target, kind, savors );
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

        void DoAddProjectReference( Project target, ArtifactDependencyKind kind, CKTrait? applicableSavors )
        {
            Debug.Assert( _solution != null );
            var r = new ProjectReference( this, target, kind, CheckSavors( applicableSavors ) );
            _projectReferences.Add( r );
            _solution.OnProjectReferenceAdded( r );
        }

        void DoRemoveProjectReferenceAt( int idx )
        {
            Debug.Assert( _solution != null );
            var r = _projectReferences[idx];
            _projectReferences.RemoveAt( idx );
            _solution.OnProjectReferenceRemoved( r );
        }

        /// <summary>
        /// Gets the package references.
        /// </summary>
        public IReadOnlyList<ProjectPackageReference> PackageReferences => _packageReferences;

        /// <summary>
        /// Adds a new <see cref="ProjectPackageReference"/> to this project.
        /// </summary>
        /// <param name="target">The referenced package.</param>
        /// <param name="kind">The dependency kind.</param>
        /// <param name="applicableSavors">Optional savors that, when defined, must be a subset of this <see cref="Savors"/>.</param>
        public void AddPackageReference( ArtifactInstance target, ArtifactDependencyKind kind, CKTrait? applicableSavors = null )
        {
            CheckSolution();
            Throw.CheckState( $"Package '{target.Artifact}' is already referenced by '{ToString()}'.", !_packageReferences.Any( p => p.Target.Artifact == target.Artifact ) );
            DoAddPackageReference( target, kind, applicableSavors ?? Savors );
        }

        /// <summary>
        /// Ensures that a new <see cref="ProjectPackageReference"/> with the exact same target and kind exists.
        /// </summary>
        /// <param name="target">The referenced package.</param>
        /// <param name="kind">The dependency kind.</param>
        /// <param name="applicableSavors">Optional savors that, when defined, must be a subset of this <see cref="Savors"/>.</param>
        public void EnsurePackageReference( ArtifactInstance target, ArtifactDependencyKind kind, CKTrait? applicableSavors = null )
        {
            CheckSolution();
            var savors = applicableSavors ?? Savors;
            int idx = _packageReferences.IndexOf( p => p.Target.Artifact == target.Artifact );
            if( idx >= 0 )
            {
                var exists = _packageReferences[idx];
                if( exists.Kind == kind
                    && exists.Target == target
                    && exists.ApplicableSavors == savors ) return;
                DoRemovePackageReferenceAt( idx );
            }
            DoAddPackageReference( target, kind, savors );
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

        void DoAddPackageReference( ArtifactInstance target, ArtifactDependencyKind kind, CKTrait? applicableSavors )
        {
            Debug.Assert( _solution != null );
            var r = new ProjectPackageReference( this, target, kind, CheckSavors( applicableSavors ) );
            _packageReferences.Add( r );
            _solution.OnPackageReferenceAdded( r );
        }

        CKTrait? CheckSavors( CKTrait? applicableSavors )
        {
            Debug.Assert( (applicableSavors == null && Savors == null) || (applicableSavors != null && Savors != null) );
            if( applicableSavors != null
                && (Savors!.Context != applicableSavors.Context || !Savors.IsSupersetOf( applicableSavors )) )
            {
                Throw.ArgumentException( $"Savors '{applicableSavors}' must be a subset of '{Savors}' (and belong to the same context).", nameof( applicableSavors ) );
            }
            return applicableSavors;
        }

        void DoRemovePackageReferenceAt( int idx )
        {
            Debug.Assert( _solution != null );
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

