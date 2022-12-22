using CK.Core;
using CK.Build;
using CK.Env.DependencyModel;
using CK.Env.NPM;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class NPMProject
    {
        Project _project;

        internal NPMProject( NPMProjectsDriver driver, IActivityMonitor m, INPMProjectSpec spec )
        {
            Driver = driver;
            Specification = spec;
            FullPath = Driver.BranchPath.Combine( spec.Folder );
            PackageJson = new PackageJsonFile( Driver.GitFolder.FileSystem, FullPath );
            if( PackageJson.Root == null )
            {
                if( Driver.GitFolder.FileSystem.GetDirectoryContents( FullPath ).Exists )
                {
                    Error( m, NPMProjectStatus.ErrorMissingPackageJson );
                }
                else
                {
                    Error( m, NPMProjectStatus.FatalInitializationError );
                }
            }
            Status = RefreshStatus( m );
            PackageJson.OnSavedOrDeleted += ( s, e ) => driver.SetSolutionDirty( e.Monitor );
        }

        /// <summary>
        /// Gets the driver plugin.
        /// </summary>
        public NPMProjectsDriver Driver { get; }

        public IProject Project => _project;

        /// <summary>
        /// Gets the project specification.
        /// </summary>
        public INPMProjectSpec Specification { get; }

        /// <summary>
        /// Gets the project status (that can be on error).
        /// </summary>
        public NPMProjectStatus Status { get; }

        /// <summary>
        /// Gets the project folder path relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath FullPath { get; }

        /// <summary>
        /// Gets the package.json file object.
        /// </summary>
        public PackageJsonFile PackageJson { get; }

        NPMProjectStatus Error( IActivityMonitor monitor, NPMProjectStatus s, string? msg = null )
        {
            monitor.Error( msg ?? $"NPM Project Error: {s} ('{FullPath}')." );
            return s;
        }

        public NPMProjectStatus RefreshStatus( IActivityMonitor m )
        {
            try
            {
                if( PackageJson.Root == null )
                {
                    return Driver.GitFolder.FileSystem.GetDirectoryContents( FullPath ).Exists
                        ? Error( m, NPMProjectStatus.ErrorMissingPackageJson )
                        : Error( m, NPMProjectStatus.FatalInitializationError );
                }
                if( Specification.IsPrivate )
                {
                    if( !PackageJson.IsPrivate ) return Error( m, NPMProjectStatus.ErrorPackageMustBePrivate );
                }
                else
                {
                    if( PackageJson.IsPrivate ) return Error( m, NPMProjectStatus.ErrorPackageMustNotBePrivate );
                    if( PackageJson.Name == null ) return Error( m, NPMProjectStatus.ErrorPackageNameMissing );
                    if( PackageJson.Name != Specification.PackageName )
                    {
                        return Error( m, NPMProjectStatus.ErrorPackageInvalidName, $"Expected package name is '{Specification.PackageName}' but found '{PackageJson.Name}'." );
                    }
                }
                return PackageJson.Refresh( m );
            }
            catch( Exception ex )
            {
                m.Error( $"While reading NPM project '{FullPath}'.", ex );
                return NPMProjectStatus.FatalInitializationError;
            }

        }

        /// <summary>
        /// This is used to generate the CodeCakeBuilder/NPMSolution.xml file that lists
        /// all the NPM projects that CodeCakeBuilder must handle.
        /// </summary>
        /// <returns>The Xml element.</returns>
        public XElement ToXml()
        {
            return new XElement( "Project",
                        new XAttribute( "Path", Specification.Folder ),
                        new XAttribute( "IsPublished", PackageJson.IsPublished ),
                        new XAttribute( "OutputFolder", Specification.OutputFolder ),
                        PackageJson.Name != null ? new XAttribute( "ExpectedName", PackageJson.Name ) : null );
        }

        internal void Associate( Project p )
        {
            if( p != _project )
            {
                if( _project != null ) _project.RemoveTags<NPMProject>();
                _project = p;
                _project.Tag( this );
            }
        }

        /// <summary>
        /// Synchronizes these <see cref="PackageJsonFile.Dependencies"/> that have a non null <see cref="NPMDep.MinVersion"/>
        /// to the associated <see cref="DependencyModel.Project"/> instance's <see cref="IProject.PackageReferences"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        internal void SynchronizePackageReferences( IActivityMonitor m )
        {
            var toRemove = new HashSet<Artifact>( _project.PackageReferences.Select( r => r.Target.Artifact ) );
            foreach( var dep in PackageJson.Dependencies )
            {
                if( dep.MinVersion == null
                    && dep.Type != NPMVersionDependencyType.LocalPath
                    && dep.Type != NPMVersionDependencyType.Portal
                    && dep.Type != NPMVersionDependencyType.Workspace )
                {
                    m.Warn( $"Unable to handle NPM {dep.Kind.ToPackageJsonKey()} '{dep.RawDep}' in {PackageJson.FilePath}. Only simple minimal version, or 'file:' relative paths, or 'file' absolute path pointing to a tarball are handled." );
                }
                if( dep.MinVersion != null )
                {
                    var instance = new Artifact( NPMClient.NPMType, dep.Name ).WithVersion( dep.MinVersion );
                    toRemove.Remove( instance.Artifact );
                    _project.EnsurePackageReference( instance, dep.Kind );
                }
            }
            foreach( var noMore in toRemove ) _project.RemovePackageReference( noMore );
        }

        /// <summary>
        /// Synchronizes these <see cref="PackageJsonFile.Dependencies"/> that are <see cref="NPMVersionDependencyType.LocalPath"/>
        /// to the associated <see cref="DependencyModel.Project"/> instance's <see cref="IProject.ProjectReferences"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        internal bool SynchronizeProjectReferences( IActivityMonitor m )
        {
            var toRemove = new HashSet<IProject>( _project.ProjectReferences.Select( r => r.Target ) );
            foreach( var dep in PackageJson.Dependencies )
            {
                if( dep.Type == NPMVersionDependencyType.LocalPath )
                {
                    var path = _project.SolutionRelativeFolderPath.Combine( dep.RawDep.Substring( "file:".Length ) );
                    var mapped = _project.Solution.Projects.FirstOrDefault( d => d.SolutionRelativeFolderPath == path.ResolveDots()
                                                                                 && d.Type == "js" );
                    if( mapped == null )
                    {
                        m.Error( $"Unable to resolve local reference to project '{dep.RawDep}' in {PackageJson}." );
                        return false;
                    }
                    _project.EnsureProjectReference( mapped, dep.Kind );
                    toRemove.Remove( mapped );
                }
            }
            foreach( var noMore in toRemove ) _project.RemoveProjectReference( noMore );
            return true;
        }

        public override string ToString() => PackageJson.ToString();
    }
}
