using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NPM;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            _driver.OnSolutionConfiguration += OnSolutionConfiguration;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( nameof( NPMProjectsDriver ) );

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

        static void SynchronizePackageReferences( IActivityMonitor m, Project project )
        {
            var toRemove = new HashSet<Artifact>( project.PackageReferences.Select( r => r.Target.Artifact ) );
            var p = project.Tag<NPMProject>();
            foreach( var dep in p.PackageJson.Dependencies )
            {
                if( dep.MinVersion == null && dep.Type != NPMVersionDependencyType.LocalPath )
                {
                    m.Warn( $"Unable to handle NPM {dep.Kind.ToPackageJsonKey()} '{dep.RawDep}' in {p.PackageJson.FilePath}. Only Simple version and file: relative paths are handled." );
                }
                if( dep.MinVersion == null )
                {
                    var instance = new Artifact( NPMClient.NPMType, dep.Name ).WithVersion( dep.MinVersion );
                    toRemove.Remove( instance.Artifact );
                    project.EnsurePackageReference( instance, dep.Kind );
                }
            }
            foreach( var noMore in toRemove ) project.RemovePackageReference( noMore );
        }

        static bool SynchronizeProjectReferences( IActivityMonitor m, Project project, Func<NormalizedPath, Project> depsFinder )
        {
            var p = project.Tag<NPMProject>();
            var toRemove = new HashSet<IProject>( project.ProjectReferences.Select( r => r.Target ) );
            foreach( var dep in p.PackageJson.Dependencies )
            {
                if( dep.Type == NPMVersionDependencyType.LocalPath )
                {
                    var path = project.SolutionRelativeFolderPath.Combine( dep.RawDep.Substring( "file:".Length ) );
                    var mapped = depsFinder( path );
                    if( mapped == null )
                    {
                        m.Error( $"Unable to resolve local reference to project '{dep.RawDep}' in {p.PackageJson}." );
                        return false;
                    }
                    project.EnsureProjectReference( mapped, dep.Kind );
                    toRemove.Remove( mapped );
                }
            }
            foreach( var noMore in toRemove ) project.RemoveProjectReference( noMore );
            return true;
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
    }
}
