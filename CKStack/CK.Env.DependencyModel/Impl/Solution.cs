using CK.Core;
using CK.Build;
using CK.Setup;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Generic solution: contains a list of <see cref="Project"/> of any type.
    /// </summary>
    public class Solution : TaggedObject, IDependentItemContainerRef, ISolution
    {
        readonly List<Project> _projects;
        readonly List<IArtifactRepository> _artifactTargets;
        readonly List<IArtifactFeed> _artifactSources;
        readonly List<SolutionPackageReference> _solutionPackageReferences;
        readonly SolutionContext _ctx;
        Project? _buildProject;
        int _version;

        internal Solution( SolutionContext ctx, NormalizedPath fullPath, string name )
        {
            _ctx = ctx;
            FullPath = fullPath;
            Name = name;
            _projects = new List<Project>();
            _artifactTargets = new List<IArtifactRepository>();
            _artifactSources = new List<IArtifactFeed>();
            _solutionPackageReferences = new List<SolutionPackageReference>();
        }

        /// <summary>
        /// Gets the solution context that contains this solution.
        /// </summary>
        public SolutionContext Solutions => _ctx;

        ISolutionContext ISolution.Solutions => _ctx;
        ISolution IPackageReferrer.Solution => this;

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
        public Project? BuildProject
        {
            get => _buildProject;
            set
            {
                if( _buildProject != value )
                {
                    if( value != null )
                    {
                        Throw.CheckArgument( "Solution mismatch.", value.Solution == this );
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

        IProject? ISolution.BuildProject => _buildProject;

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
        public Project AddProject( NormalizedPath solutionRelativeFolderPath,
                                   string type,
                                   string simpleProjecName )
        {
            var r = AddOrFindProject( solutionRelativeFolderPath, type, simpleProjecName );
            if( !r.Created ) Throw.InvalidOperationException( $"Project at '{solutionRelativeFolderPath}' of type '{type}' is already registered in '{ToString()}'." );
            return r.Project;
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
        public (Project Project, bool Created) AddOrFindProject( NormalizedPath solutionRelativeFolderPath,
                                                                 string type,
                                                                 string simpleProjecName )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( type );
            Throw.CheckNotNullOrWhiteSpaceArgument( simpleProjecName );

            var fullFolderPath = FullPath.Combine( solutionRelativeFolderPath );
            var newOne = new Project( this, solutionRelativeFolderPath, fullFolderPath, type, simpleProjecName, null );
            Debug.Assert( newOne.Name.Length == 0 );
            var added = _ctx.OnProjectAdding( newOne );
            if( added != newOne ) return (added, false);
            Debug.Assert( newOne.Name.Length > 0 );
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
            if( project.Solution != this ) Throw.ArgumentException( nameof( project ), "Solution mismatch." );
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
        /// Adds a new artifact target to <see cref="ArtifactTargets"/> if it isn't already present.
        /// </summary>
        /// <param name="newOne">New artifact target.</param>
        /// <returns>True if the repository has been added, false otherwise.</returns>
        public bool AddArtifactTarget( IArtifactRepository newOne )
        {
            if( !_artifactTargets.Contains( newOne ) )
            {
                _artifactTargets.Add( newOne );
                OnArtifactTargetAdded( newOne );
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the artifact target from <see cref="ArtifactTargets"/> if it exists.
        /// </summary>
        /// <param name="artifactTarget">The artifact target.</param>
        /// <returns>True if the repository has been removed, false otherwise.</returns>
        public bool RemoveArtifactTarget( IArtifactRepository artifactTarget )
        {
            int idx = _artifactTargets.IndexOf( artifactTarget );
            if( idx >= 0 )
            {
                _artifactTargets.RemoveAt( idx );
                OnArtifactTargetRemoved( artifactTarget );
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the artifacts sources.
        /// </summary>
        public IReadOnlyCollection<IArtifactFeed> ArtifactSources => _artifactSources;

        /// <summary>
        /// Adds a new artifact source to <see cref="ArtifactSources"/> if it's not already present.
        /// </summary>
        /// <param name="newOne">New artifact source.</param>
        /// <returns>True if the feed has been added, false otherwise.</returns>
        public bool AddArtifactSource( IArtifactFeed newOne )
        {
            if( !_artifactSources.Contains( newOne ) )
            {
                _artifactSources.Add( newOne );
                OnArtifactSourceAdded( newOne );
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the artifact source from <see cref="ArtifactSources"/> if it exists.
        /// </summary>
        /// <param name="artifactSource">The artifact source.</param>
        /// <returns>True if the feed has been removed, false otherwise.</returns>
        public bool RemoveArtifactSource( IArtifactFeed artifactSource )
        {
            int idx = _artifactSources.IndexOf( artifactSource );
            if( idx >= 0 )
            {
                _artifactSources.RemoveAt( idx );
                OnArtifactSourceRemoved( artifactSource );
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets a set of projectless dependencies.
        /// </summary>
        public IReadOnlyCollection<SolutionPackageReference> SolutionPackageReferences => _solutionPackageReferences;

        /// <summary>
        /// Adds a direct dependency to this solution in <see cref="SolutionPackageReferences"/> if it's not already present.
        /// </summary>
        /// <param name="target">Referenced package.</param>
        /// <returns>True if the dependency has been added, false otherwise.</returns>
        public bool AddSolutionPackageReference( ArtifactInstance target )
        {
            Throw.CheckArgument( target.IsValid  );

            int idx = _solutionPackageReferences.IndexOf( p => p.Target.Artifact == target.Artifact );
            if( idx >= 0 && _solutionPackageReferences[idx].Target.Version == target.Version ) return false;

            var newOne = new SolutionPackageReference( this, target );
            if( idx >= 0 ) _solutionPackageReferences[idx] = newOne;
            else _solutionPackageReferences.Add( newOne );
            OnSolutionPackageReferenceChanged();
            return true;
        }

        /// <summary>
        /// Removes a direct dependency from <see cref="SolutionPackageReferences"/> if it exists.
        /// </summary>
        /// <param name="target">The artifact to remove.</param>
        /// <returns>True if the dependency has been found and removed, false otherwise.</returns>
        public bool RemoveSolutionPackageReference( Artifact target )
        {
            int idx = _solutionPackageReferences.IndexOf( p => p.Target.Artifact == target );
            if( idx >= 0 )
            {
                var oldOne = _solutionPackageReferences[idx];
                _solutionPackageReferences.RemoveAt( idx );
                OnSolutionPackageReferenceChanged();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the <see cref="SolutionPackageReferences"/> concatenated to all <see cref="IProject.PackageReferences"/>.
        /// This is the whole set of references from this solution.
        /// </summary>
        public IEnumerable<PackageReference> AllPackageReferences => _solutionPackageReferences.Select( p => new PackageReference( p.Owner, p.Target ) )
                                                                        .Concat( _projects.SelectMany( p => p.PackageReferences.Select( pr => new PackageReference( pr.Owner, pr.Target ) ) ) );

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

        internal void OnProjectSavorsTransformed( Project project )
        {
            _version++;
            _ctx.OnProjectSavorsTransformed( project );
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

        internal void OnPackageReferenceRemoved( in ProjectPackageReference r )
        {
            _version++;
            _ctx.OnPackageReferenceRemoved( r );
        }

        internal void OnPackageReferenceAdded( in ProjectPackageReference r )
        {
            _version++;
            _ctx.OnPackageReferenceAdded( r );
        }

        internal void OnPackageReferenceUpdated( in ProjectPackageReference r )
        {
            _version++;
            _ctx.OnPackageReferenceUpdated( r );
        }

        internal void OnProjectReferenceAdded( in ProjectReference r )
        {
            _version++;
            _ctx.OnProjectReferenceAdded( r );
        }

        internal void OnProjectReferenceRemoved( in ProjectReference r )
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
        void OnArtifactSourceAdded( IArtifactFeed newOne )
        {
            _version++;
            _ctx.OnArtifactSourceAdded( this, newOne );
        }

        void OnArtifactSourceRemoved( IArtifactFeed artifactSource )
        {
            _version++;
            _ctx.OnArtifactSourceRemoved( this, artifactSource );
        }

        void OnSolutionPackageReferenceChanged()
        {
            _version++;
            _ctx.OnSolutionPackageReferenceChanged();
        }

    }
}
