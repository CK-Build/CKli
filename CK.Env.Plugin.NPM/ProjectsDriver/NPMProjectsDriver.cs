using CK.Core;
using CK.Env.NPM;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace CK.Env.Plugin
{
    public class NPMProjectsDriver : GitBranchPluginBase, ICommandMethodsProvider
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _spec;
        NPMProject[] _npmProjects;

        public NPMProjectsDriver( GitFolder f, NormalizedPath branchPath, SolutionDriver driver, SolutionSpec spec, IEnvLocalFeedProvider localFeedProvider )
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
            foreach( var p in _npmProjects )
            {
                var (project,isNew) = e.Solution.AddOrFindProject( p.Specification.Folder, "js", p.PackageJson.SafeName );
                p.Associate( project );
                if( isNew )
                {
                    if( p.PackageJson.IsPublished )
                    {
                        project.AddGeneratedArtifacts( new Artifact( NPMClient.NPMType, p.PackageJson.Name ) );
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
            return _driver.GetSolution( m ) != null ? _npmProjects : null;
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

    }
}
