using CK.Setup;
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
    /// Generic solution: contains a list of <see cref="Project"/> of any type.
    /// </summary>
    public class Solution : TaggedObject, IDependentItemContainerRef, ISolution
    {
        readonly List<Project> _projects;
        readonly List<IArtifactRepository> _artifactTargets;
        SolutionContext _ctx;
        Project _buildProject;
        int _version;

        internal Solution( SolutionContext ctx, NormalizedPath fullPath, string name )
        {
            _ctx = ctx;
            FullPath = fullPath;
            Name = name;
            _projects = new List<Project>();
            _artifactTargets = new List<IArtifactRepository>();
        }

        /// <summary>
        /// Gets the solution context that contains this solution.
        /// </summary>
        public SolutionContext Solutions => _ctx;

        ISolutionContext ISolution.Solutions => _ctx;

        /// <summary>
        /// Gets the current version. This changes each time
        /// anything changes in this solution or its projects.
        /// </summary>
        public int Version => _version;

        /// <summary>
        /// Gets the full path of this solution that must be unique across <see cref="Solutions"/>.
        /// </summary>
        public NormalizedPath FullPath { get; }

        /// <summary>
        /// Gets the solution name that must uniquely identify a solution among multiple solutions.
        /// This is not necessarily the last part of its <see cref="FullPath"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets all the generated artifacts from all <see cref="Projects"/>.
        /// </summary>
        public IEnumerable<GeneratedArtifact> GeneratedArtifacts => _projects.SelectMany( p => p.GeneratedArtifacts );

        internal void CheckNewArtifact( Artifact a )
        {
            if( !a.IsValid ) throw new ArgumentException( "Invalid artifact.", nameof( a ) );
            if( GeneratedArtifacts.Select( g => g.Artifact.TypedName ).Contains( a.TypedName ) )
            {
                throw new InvalidOperationException( $"Artifact '{a}' is already generated by '{ToString()}'." );
            }
        }

        /// <summary>
        /// Gets all the projects.
        /// </summary>
        public IReadOnlyList<Project> Projects => _projects;

        IReadOnlyList<IProject> ISolution.Projects => _projects;

        /// <summary>
        /// Clears the <see cref="Projects"/>.
        /// </summary>
        public void ClearProjects()
        {
            while( _projects.Count > 0 )
            {
                RemoveProject( _projects[_projects.Count - 1] );
            }
        }

        /// <summary>
        /// Gets or sets the build project. Can be null.
        /// When not null, the project must belong to this <see cref="Projects"/> and both <see cref="Project.IsPublished"/>
        /// and <see cref="Project.IsTestProject"/> must be false.
        /// </summary>
        public Project BuildProject
        {
            get => _buildProject;
            set
            {
                if( _buildProject != value )
                {
                    if( value != null )
                    {
                        if( value.Solution != this ) throw new ArgumentException( "Solution mismatch.", nameof(value) );
                        if( value.IsPublished || value.IsTestProject )
                        {
                            throw new InvalidOperationException( $"Project {ToString()} must not be Published nor be a Test project to be the Solution's BuildProject." );
                        }
                    }
                    _buildProject = value;
                    OnBuildProjectChanged();
                }
            }
        }

        IProject ISolution.BuildProject => _buildProject;

        /// <summary>
        /// Appends a new project to the <see cref="Projects"/> list if no project with the
        /// same <see cref="IProject.SimpleProjectName"/>, <see cref="IProject.Type"/>
        /// and <see cref="IProject.FullFolderPath"/> already exists in the other <see cref="Solutions"/>.
        /// If name clashes, an <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        /// <param name="solutionRelativeFolderPath">
        /// The path to the project relative to this <see cref="Solution"/>
        /// </param>
        /// <param name="type">The project type.</param>
        /// <param name="simpleProjecName">The project name.</param>
        /// <returns>The new project.</returns>
        public Project AddProject( NormalizedPath solutionRelativeFolderPath, string type, string simpleProjecName )
        {
            var r = AddOrFindProject( solutionRelativeFolderPath, type, simpleProjecName );
            if( !r.Created ) throw new InvalidOperationException( $"Project at '{solutionRelativeFolderPath}' of type '{type}' is already registered in '{ToString()}'." );
            return r.Item1;
        }

        /// <summary>
        /// Appends a new project to the <see cref="Projects"/> list or finds the one with the
        /// same <see cref="IProject.SimpleProjectName"/>, <see cref="IProject.Type"/> and <see cref="IProject.FullFolderPath"/>.
        /// </summary>
        /// <param name="solutionRelativeFolderPath">
        /// The path to the project relative to this <see cref="Solution"/>
        /// </param>
        /// <param name="type">The project type.</param>
        /// <param name="simpleProjecName">The project name.</param>
        /// <returns>The project and whether it has been created or not.</returns>
        public (Project Project, bool Created) AddOrFindProject( NormalizedPath solutionRelativeFolderPath, string type, string simpleProjecName )
        {
            if( String.IsNullOrWhiteSpace( type ) ) throw new ArgumentNullException( nameof( type ) );
            if( String.IsNullOrWhiteSpace( simpleProjecName ) ) throw new ArgumentNullException( nameof( simpleProjecName ) );
            var fullFolderPath = FullPath.Combine( solutionRelativeFolderPath );
            var newOne = new Project( this, solutionRelativeFolderPath, fullFolderPath, type, simpleProjecName );
            Debug.Assert( newOne.Name == null );
            var added = _ctx.OnProjectAdding( newOne );
            if( added != newOne ) return (added, false);
            Debug.Assert( newOne.Name != null );
            _projects.Add( newOne );
            OnProjectAdded( newOne );
            return (newOne, true);
        }

        /// <summary>
        /// Removes the project (that must belong to this solution otherwise an exception is thrown).
        /// </summary>
        /// <param name="project"></param>
        public void RemoveProject( Project project )
        {
            project.CheckSolution();
            if( project.Solution != this ) throw new ArgumentException( "Solution mismatch.", nameof( project ) );
            Debug.Assert( _projects.Contains( project ) );
            project.IsBuildProject = false;
            _projects.Remove( project );
            // Raise event before detach so that solution's project is available.
            OnProjectRemoved( project );
            project.Detach();
        }

        /// <summary>
        /// Gets the repositories where produced artifacts must be pushed.
        /// </summary>
        public IReadOnlyCollection<IArtifactRepository> ArtifactTargets => _artifactTargets;

        /// <summary>
        /// Adds a new artifact target (that must not already belong to <see cref="ArtifactTargets"/> otherwise an
        /// InvalidOperationException is thrown).
        /// </summary>
        /// <param name="newOne">New artifact target.</param>
        public void AddArtifactTarget( IArtifactRepository newOne )
        {
            if( _artifactTargets.Contains( newOne ) ) throw new InvalidOperationException( $"Artifact target already registered." );
            _artifactTargets.Add( newOne );
            OnArtifactTargetAdded( newOne );
        }

        /// <summary>
        /// Removes the artifact target (that must belong to <see cref="ArtifactTargets"/> otherwise an
        /// InvalidOperationException is thrown).
        /// </summary>
        /// <param name="artifactTarget">The artifact target.</param>
        public void RemoveArtifactTArget( IArtifactRepository artifactTarget )
        {
            if( !_artifactTargets.Contains( artifactTarget ) ) throw new InvalidOperationException( $"Artifact target not registered." );
            _artifactTargets.Remove( artifactTarget );
            OnArtifactTargetRemoved( artifactTarget );
        }

        string IDependentItemRef.FullName => Name;

        bool IDependentItemRef.Optional => false;

        /// <summary>
        /// Overridden to return the <see cref="Name"/>.
        /// </summary>
        /// <returns>The solution's name.</returns>
        public override string ToString() => Name;

        void OnProjectAdded( Project newOne )
        {
            _version++;
            _ctx.OnProjectAdded( newOne );
        }

        void OnProjectRemoved( Project project )
        {
            _version++;
            _ctx.OnProjectRemoved( project );
        }

        void OnBuildProjectChanged()
        {
            _version++;
            _ctx.OnBuildProjectChanged( this );
        }

        internal void OnIsTestProjectChanged( Project project )
        {
            _version++;
            _ctx.OnIsTestProjectChanged( project );
        }

        internal void OnIsPublishedChange( Project project )
        {
            _version++;
            _ctx.OnIsPublishedChange( project );
        }

        internal void OnArtifactAdded( Artifact a, Project project )
        {
            _version++;
            _ctx.OnArtifactAdded( a, project );
        }

        internal void OnArtifactRemoved( Artifact a, Project project )
        {
            _version++;
            _ctx.OnArtifactRemoved( a, project );
        }

        internal void OnPackageReferenceRemoved( PackageReference r )
        {
            _version++;
            _ctx.OnPackageReferenceRemoved( r );
        }

        internal void OnPackageReferenceAdded( PackageReference r )
        {
            _version++;
            _ctx.OnPackageReferenceAdded( r );
        }

        internal void OnProjectReferenceAdded( ProjectReference r )
        {
            _version++;
            _ctx.OnProjectReferenceAdded( r );
        }

        internal void OnProjectReferenceRemoved( ProjectReference r )
        {
            _version++;
            _ctx.OnProjectReferenceRemoved( r );
        }

        void OnArtifactTargetAdded( IArtifactRepository newOne )
        {
            _version++;
            _ctx.OnArtifactTargetAdded( this, newOne );
        }

        void OnArtifactTargetRemoved( IArtifactRepository artifactTarget )
        {
            _version++;
            _ctx.OnArtifactTargetRemoved( this, artifactTarget );
        }

    }
}
