using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NPM;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.Plugin
{
    public class NPMProjectsDriver : GitBranchPluginBase, ICommandMethodsProvider, IDisposable
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _spec;
        NPMProject[] _npmProjects;
        AngularWorkspace[] _angularWorkspaces;

        IEnumerable<NPMProject> AllNpmProjects => _npmProjects.Concat( _angularWorkspaces.SelectMany( p => p.Projects ) );
        public NPMProjectsDriver( GitFolder f, NormalizedPath branchPath, SolutionDriver driver, SolutionSpec spec )
            : base( f, branchPath )
        {
            _driver = driver;
            _spec = spec;
            _driver.OnSolutionConfiguration += OnSolutionConfiguration;
            _driver.OnUpdatePackageDependency += OnUpdatePackageDependency;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( nameof( NPMProjectsDriver ) );

        /// <summary>
        /// Forces the solution to be reloaded.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void SetSolutionDirty( IActivityMonitor m ) => _driver.SetSolutionDirty( m );

        void OnSolutionConfiguration( object sender, SolutionConfigurationEventArgs e )
        {
            if( !ReadNPMProjects( e.Monitor ) )
            {
                e.PreventSolutionUse( "NPM project error." );
                return;
            }
            if( !ReadAngularWorkspace( e.Monitor ) )
            {
                e.PreventSolutionUse( "NPM project error inside an Angular Workspace." );
            }

            bool somePublished = false;
            //Dependency model stuff: ensuring project, adding generated artifacts and synchronize the package references.
            foreach( var p in AllNpmProjects )
            {
                var (project, isNew) = e.Solution.AddOrFindProject( p.Specification.Folder, "js", p.PackageJson.SafeName );
                p.Associate( project );
                if( isNew )
                {
                    if( p.PackageJson.IsPublished )//add generated artifact if needed.
                    {
                        project.AddGeneratedArtifacts( new Artifact( NPMClient.NPMType, p.PackageJson.Name ) );
                        somePublished = true;
                    }
                }
                p.SynchronizePackageReferences( e.Monitor );
            }


            foreach( var p in AllNpmProjects )
            {
                if( !p.SynchronizeProjectReferences( e.Monitor ) ) //Project reference
                {
                    e.PreventSolutionUse( "NPM project path relative error." );
                }
            }
            if( !AllNpmProjects.Any() )
            {
                var sources = e.Solution.ArtifactSources.Where( s => s.ArtifactType == NPMClient.NPMType ).ToList();
                if( sources.Count > 0 )
                {
                    e.Monitor.Info( $"Removing sources: {sources.Select( t => t.TypedName ).Concatenate()} since there is no NPM projetcs." );
                    foreach( var s in sources ) e.Solution.RemoveArtifactSource( s );
                }
            }
            if( !somePublished )
            {
                var targets = e.Solution.ArtifactTargets.Where( t => t.HandleArtifactType( NPMClient.NPMType ) ).ToList();
                if( targets.Count > 0 )
                {
                    e.Monitor.Info( $"Removing targets: {targets.Select( t => t.UniqueRepositoryName ).Concatenate()} since no published NPM projetcs exist." );
                    foreach( var t in targets ) e.Solution.RemoveArtifactTarget( t );
                }
            }
        }

        bool ReadNPMProjects( IActivityMonitor m )
        {
            if( _npmProjects == null )
            {
                _npmProjects = new NPMProject[_spec.NPMProjects.Count];
                int i = 0;
                foreach( var spec in _spec.NPMProjects )
                {
                    _npmProjects[i++] = new NPMProject( this, m, spec );
                }
            }
            return _npmProjects.All( p => p.RefreshStatus( m ) == NPMProjectStatus.Valid );
        }

        bool ReadAngularWorkspace( IActivityMonitor m )
        {
            if( _angularWorkspaces == null )
            {
                _angularWorkspaces = new AngularWorkspace[_spec.AngularWorkspaces.Count];
                int i = 0;
                foreach( var spec in _spec.AngularWorkspaces )
                {
                    _angularWorkspaces[i++] = AngularWorkspace.LoadAngularSolution( this, m, spec );
                }
            }
            return _angularWorkspaces.SelectMany( s => s.Projects ).All( p => p.RefreshStatus( m ) == NPMProjectStatus.Valid );
        }

        public IReadOnlyList<NPMProject> GetNPMProjects( IActivityMonitor m )
        {
            return _driver.GetSolution( m, allowInvalidSolution: true ) != null ? _npmProjects : null;
        }
        public IReadOnlyList<AngularWorkspace> GetAngularWorkspaces( IActivityMonitor m )
        {
            return _driver.GetSolution( m, allowInvalidSolution: true ) != null ? _angularWorkspaces : null;
        }

        void OnUpdatePackageDependency( object sender, UpdatePackageDependencyEventArgs e )
        {
            bool mustSave = false;
            foreach( var update in e.UpdateInfo )
            {
                if( update.Referer is IProject project )
                {
                    var p = project.Tag<NPMProject>();
                    if( p != null )
                    {
                        Debug.Assert( _npmProjects.Contains( p ) );
                        mustSave |= p.PackageJson.SetDependencyMinVersion( e.Monitor, update.PackageUpdate.Artifact.Name, update.PackageUpdate.Version );
                    }
                }
            }
            if( mustSave )
            {
                foreach( var p in _npmProjects ) p.PackageJson.Save( e.Monitor );
            }
        }

        void IDisposable.Dispose()
        {
            _driver.OnSolutionConfiguration -= OnSolutionConfiguration;
            _driver.OnUpdatePackageDependency -= OnUpdatePackageDependency;
        }
    }
}
