using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Actual project that is not a <see cref="SolutionFolder"/>.
    /// </summary>
    public class Project : ProjectBase
    {
        readonly Dictionary<string, PropertyLine> _versionControlProperties;
        readonly Dictionary<string, PropertyLine> _platformConfigurationProperties;

        internal Project(
                    SolutionFile solution,
                    string projectGuid,
                    string projectTypeGuid,
                    string projectName,
                    NormalizedPath relativePath )
            : base( solution, projectGuid, projectTypeGuid, projectName, relativePath )
        {
            _versionControlProperties = new Dictionary<string, PropertyLine>( StringComparer.OrdinalIgnoreCase );
            _platformConfigurationProperties = new Dictionary<string, PropertyLine>( StringComparer.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Gets the version control lines for this project.
        /// </summary>
        public IReadOnlyCollection<PropertyLine> VersionControlLines => _versionControlProperties.Values;

        /// <summary>
        /// Adds a new line in <see cref="VersionControlLines"/>.
        /// </summary>
        /// <param name="p">The new line.</param>
        public void AddVersionControl( PropertyLine p )
        {
            _platformConfigurationProperties.Add( p.Name, p );
            Solution.SetDirtyStructure( true );
        }

        /// <summary>
        /// Gets the project platform configurations.
        /// </summary>
        public IReadOnlyCollection<PropertyLine> ProjectConfigurationPlatformsLines => _platformConfigurationProperties.Values;

        /// <summary>
        /// Adds a new project configuration entry in <see cref="ProjectConfigurationPlatformsLines"/>.
        /// </summary>
        /// <param name="p"></param>
        public void AddProjectConfigurationPlatform( PropertyLine p )
        {
            _platformConfigurationProperties.Add( p.Name, p );
            Solution.SetDirtyStructure( true );
        }

        /// <summary>
        /// Reads the solution level dependencies.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="dep">The action per dependent project.</param>
        /// <returns>True on success, false on error.</returns>
        protected bool EnumerateSolutionLevelProjectDependencies( IActivityMonitor m, Action<Project> dep )
        {
            var deps = FindSection( "ProjectDependencies" );
            if( deps != null )
            {
                foreach( var line in deps.PropertyLines )
                {
                    var p = Solution.FindProjectByGuid<Project>( m, line.Name, line.LineNumber );
                    if( p == null ) return false;
                    dep( p );
                }
            }
            return true;
        }

        public override string ToString() => $"Project '{SolutionRelativeLogicalFolderPath}'";

    }
}
