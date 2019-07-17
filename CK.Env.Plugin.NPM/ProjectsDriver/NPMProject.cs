using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NPM;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class NPMProject
    {
        readonly PackageJsonFile _packageFile;
        INPMProjectSpec _spec;
        NPMProjectStatus _status;
        Project _project;

        internal NPMProject( NPMProjectsDriver driver, IActivityMonitor m, INPMProjectSpec spec )
        {
            Driver = driver;
            _spec = spec;
            FullPath = Driver.BranchPath.Combine( spec.Folder );
            _packageFile = new PackageJsonFile( this );
            _status = RefreshStatus( m );
            _packageFile.OnSavedOrDeleted += (s,e) => driver.SetSolutionDirty( e.Monitor );
        }

        /// <summary>
        /// Gets the driver plugin.
        /// </summary>
        public NPMProjectsDriver Driver { get; }

        public IProject Project => _project;

        /// <summary>
        /// Gets the project specification.
        /// </summary>
        public INPMProjectSpec Specification => _spec;

        /// <summary>
        /// Gets the project status (that can be on error).
        /// </summary>
        public NPMProjectStatus Status => _status;

        /// <summary>
        /// Gets the project folder path relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath FullPath { get; }

        /// <summary>
        /// Gets the package.json file object.
        /// </summary>
        public PackageJsonFile PackageJson => _packageFile;

        public NPMProjectStatus RefreshStatus( IActivityMonitor m )
        {
            NPMProjectStatus Error( NPMProjectStatus s, string msg = null )
            {
                m.Error( msg ?? $"Error: {s}" );
                return s;
            }
            try
            {
                if( _packageFile.Root == null )
                {
                    return Driver.GitFolder.FileSystem.GetDirectoryContents( FullPath ).Exists
                        ? Error( NPMProjectStatus.ErrorMissingPackageJson )
                        : Error( NPMProjectStatus.FatalInitializationError );
                }
                if( _spec.IsPrivate )
                {
                    if( !_packageFile.IsPrivate ) return Error( NPMProjectStatus.ErrorPackageMustBePrivate );
                }
                else
                {
                    if( _packageFile.IsPrivate ) return Error( NPMProjectStatus.ErrorPackageMustNotBePrivate );
                    if( _packageFile.Name == null ) return Error( NPMProjectStatus.ErrorPackageNameMissing );
                    if( _packageFile.Name != _spec.PackageName )
                    {
                        return Error( NPMProjectStatus.ErrorPackageInvalidName, $"Expected package name is '{_spec.PackageName}' but found '{_packageFile.Name}'." );
                    }
                }
                return _packageFile.Refresh( m );
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
                        new XAttribute( "Path", _spec.Folder ),
                        new XAttribute( "IsPublished", PackageJson.IsPublished ),
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
                if( dep.MinVersion == null && dep.Type != NPMVersionDependencyType.LocalPath )
                {
                    m.Warn( $"Unable to handle NPM {dep.Kind.ToPackageJsonKey()} '{dep.RawDep}' in {PackageJson.FilePath}. Only simple minimal version and 'file:' relative paths are handled." );
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

        public override string ToString() => _packageFile.ToString();
    }
}
