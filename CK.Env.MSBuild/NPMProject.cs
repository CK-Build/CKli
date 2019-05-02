using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CK.Core;
using CK.Env.NPM;
using CK.Text;

namespace CK.Env.MSBuild
{

    public class NPMProject : NPM.INPMProject, IP
    {
        NPMProjectStatus _status;
        List<ProjectToProjectDependency> _projectDependencies;
        NPM.INPMProject _p;

        internal NPMProject( Solution s, NPM.INPMProject p )
        {
            PrimarySolution = s;
            _p = p;
        }

        /// <summary>
        /// Gets the <see cref="PrimarySolution"/>.
        /// </summary>
        public Solution PrimarySolution { get; }

        public string ProjectType => "NPM";

        public string ProjectName => PackageJson.IsPublished ? PackageJson.Name : PrimarySolutionRelativeFolderPath.Path;

        public bool IsPublished => PackageJson.IsPublished;

        /// <summary>
        /// Gets the project folder path relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath FullPath => _p.FullPath;

        public NormalizedPath PrimarySolutionRelativeFolderPath => FullPath.RemovePrefix( PrimarySolution.SolutionFolderPath );

        public PackageJsonFile PackageJson => _p.PackageJson;

        public INPMProjectSpec Specification => _p.Specification;

        public NPMProjectStatus Status => _status;

        /// <summary>
        /// Gets the NPM projects in this <see cref="PrimarySolution"/> that are dependencies.
        /// Never null.
        /// </summary>
        public IReadOnlyList<ProjectToProjectDependency> ProjectDependencies => (IReadOnlyList<ProjectToProjectDependency>)_projectDependencies ?? Array.Empty<ProjectToProjectDependency>();

        public NPMProjectStatus RefreshStatus( IActivityMonitor m ) => DoRefreshStatus( m );

        public XElement ToXml()
        {
            return new XElement( "Project",
                        new XAttribute( "Path", FullPath.RemovePrefix( PrimarySolution.SolutionFolderPath ) ),
                        new XAttribute( "IsPublished", PackageJson.IsPublished ),
                        PackageJson.Name != null ? new XAttribute( "ExpectedName", PackageJson.Name ) : null );
        }

        NPMProjectStatus DoRefreshStatus( IActivityMonitor m )
        {
            _projectDependencies = null;
            _status = _p.RefreshStatus( m );
            if( _status == NPMProjectStatus.Valid )
            {
                if( !FillProjectToProjectDependencies( m ) )
                {
                    _status = NPMProjectStatus.FatalInitializationError;
                }
            }
            return _status;
        }

        /// <summary>
        /// Captures a dependency to another <see cref="NPMProject"/>.
        /// </summary>
        public struct ProjectToProjectDependency
        {
            public readonly NPMProject Project;
            public readonly DependencyKind Kind;

            internal ProjectToProjectDependency( NPMProject p, DependencyKind k )
            {
                Project = p;
                Kind = k;
            }

        }

        /// <summary>
        /// Gets the NPM projects in this <see cref="PrimarySolution"/> that are dependencies.
        /// Note that any <see cref="VersionDependencyType.LocalPath"/> that references absolute paths or
        /// projects outside this <see cref="PrimarySolution"/> is an error.
        /// </summary>
        /// <param name="m">The activity monitor.</param>
        /// <returns>True on success, false on error.</returns>
        bool FillProjectToProjectDependencies( IActivityMonitor m )
        {
            Debug.Assert( _projectDependencies == null );
            var result = new List<ProjectToProjectDependency>();
            foreach( var pDep in _p.PackageJson
                                                .Dependencies
                                                .Where( d => d.Type == VersionDependencyType.LocalPath )
                                                .Select( d => (d.Kind,
                                                                Path: _p.PackageJson
                                                                .FilePath
                                                                .RemoveLastPart()
                                                                .Combine( d.RawDep.Substring( "file:".Length ) )
                                                                .ResolveDots()) ) )
            {
                if( !pDep.Path.StartsWith( PrimarySolution.SolutionFolderPath, strict: true ) )
                {
                    m.Error( $"Project {ToString()} has file dependency {pDep.Path} that is outside the parent Solution." );
                    return false;
                }
                var p = PrimarySolution.NPMProjects.FirstOrDefault( d => d.Specification.FullPath == pDep.Path );
                if( p == null )
                {
                    m.Error( $"Project {ToString()} has file dependency {pDep.Path} that is not declared as a NPM project." );
                    return false;
                }
                result.Add( new ProjectToProjectDependency( p, pDep.Kind ) );
            }
            _projectDependencies = result;
            return true;
        }

        public override string ToString() => $"{PrimarySolution}/{_p.ToString()}";
    }
}
