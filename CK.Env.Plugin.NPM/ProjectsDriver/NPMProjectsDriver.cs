using CK.Core;
using CK.Env.NPM;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace CK.Env.Plugin
{
    public class NPMProjectsDriver : GitBranchPluginBase, ICommandMethodsProvider, IDisposable
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _spec;
        NPMProject[] _npmProjects;
        
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
            bool somePublished = false;
            foreach( var p in _npmProjects )
            {
                var (project,isNew) = e.Solution.AddOrFindProject( p.Specification.Folder, "js", p.PackageJson.SafeName );
                p.Associate( project );
                if( isNew )
                {
                    if( p.PackageJson.IsPublished )
                    {
                        project.AddGeneratedArtifacts( new Artifact( NPMClient.NPMType, p.PackageJson.Name ) );
                        somePublished = true;
                    }
                }
                p.SynchronizePackageReferences( e.Monitor );
            }
            foreach( var p in _npmProjects )
            {
                if( !p.SynchronizeProjectReferences( e.Monitor ) )
                {
                    e.PreventSolutionUse( "NPM project path relative error." );
                }
            }
            if( _npmProjects.Length == 0 )
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
                bool valid = false;
                int i = 0;
                foreach( var spec in _spec.NPMProjects )
                {
                    var p = new NPMProject( this, m, spec );
                    _npmProjects[i++] = p;
                    valid &= p.Status == NPMProjectStatus.Valid;
                }
            }
            return _npmProjects.All( p => p.RefreshStatus( m ) == NPMProjectStatus.Valid );
        }

        public IReadOnlyList<NPMProject> GetNPMProjects( IActivityMonitor m )
        {
            return _driver.GetSolution( m, allowInvalidSolution: true ) != null ? _npmProjects : null;
        }

        void OnUpdatePackageDependency( object sender, UpdatePackageDependencyEventArgs e )
        {
            bool mustSave = false;
            foreach( var update in e.UpdateInfo )
            {
                var p = update.Project.Tag<NPMProject>();
                if( p != null )
                {
                    Debug.Assert( _npmProjects.Contains( p ) );
                    mustSave |= p.PackageJson.SetDependencyMinVersion( e.Monitor, update.PackageUpdate.Artifact.Name, update.PackageUpdate.Version );
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
