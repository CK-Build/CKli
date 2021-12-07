using CK.Core;

using System;
using System.Collections.Generic;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Base project for either <see cref="SolutionFolder"/> or <see cref="Project"/>.
    /// </summary>
    public abstract class ProjectBase : ISolutionItem
    {
        readonly Dictionary<string, Section> _sections;
        string? _parentFolderGuid;
        string _projectName;
        SolutionFolder? _parentFolder;

        internal ProjectBase(
            SolutionFile solution,
            string projectGuid,
            string projectTypeGuid,
            string projectName,
            NormalizedPath relativePath )
        {
            Solution = solution;
            ProjectGuid = projectGuid;
            ProjectTypeGuid = projectTypeGuid;
            KnownType = ProjectType.Parse( projectTypeGuid );
            _projectName = projectName;
            SolutionRelativePath = relativePath;
            SolutionRelativeFolderPath = relativePath.RemoveLastPart();
            Path = Solution.SolutionFolderPath.Combine( relativePath );
            _sections = new Dictionary<string, Section>( StringComparer.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Gets the solution that owns this project.
        /// </summary>
        public SolutionFile Solution { get; }

        /// <summary>
        /// Gets the project type identifier.
        /// </summary>
        internal protected string ProjectTypeGuid { get; }

        /// <summary>
        /// Gets this project known type.
        /// </summary>
        public KnownProjectType KnownType { get; }

        /// <summary>
        /// Gets the project Guid.
        /// </summary>
        public string ProjectGuid { get; }

        /// <summary>
        /// Gets the path to the project file relative to the <see cref="SolutionFile.SolutionFolderPath"/>.
        /// </summary>
        public NormalizedPath SolutionRelativePath { get; }

        /// <summary>
        /// Gets the path to the project directory relative to the <see cref="SolutionFile.SolutionFolderPath"/>.
        /// </summary>
        public NormalizedPath SolutionRelativeFolderPath { get; }

        /// <summary>
        /// Gets the project path: it is the .proj (.csproj etc.) for an actual project (relative
        /// to the <see cref="FileSystem"/>) and the folder path of a <see cref="SolutionFolder"/> .
        /// </summary>
        public NormalizedPath Path { get; }

        /// <summary>
        /// Gets or sets the project name: this is the name in the <see cref="Solution"/> file.
        /// It may not be synchronized with the actual project file name.
        /// </summary>
        public virtual string ProjectName
        {
            get => _projectName;
            set
            {
                if( _projectName != value )
                {
                    _projectName = value;
                    Solution.SetDirtyStructure( true );
                }
            }
        }

        /// <summary>
        /// Gets the project sections.
        /// </summary>
        internal protected IReadOnlyCollection<Section> ProjectSections => _sections.Values;

        /// <summary>
        /// Adds a section to <see cref="ProjectSections"/>.
        /// </summary>
        /// <param name="item">The section to add.</param>
        internal protected virtual void AddSection( Section item )
        {
            _sections.Add( item.Name, item );
            Solution.SetDirtyStructure( true );
        }

        /// <summary>
        /// Finds a section.
        /// </summary>
        /// <param name="name">Name of the section.</param>
        /// <returns>Section or null.</returns>
        internal protected Section? FindSection( string name ) => _sections.GetValueOrDefault( name, null! );

        internal string? ParentFolderGuid
        {
            get => _parentFolderGuid;
            set
            {
                if( _parentFolderGuid != value )
                {
                    if( (_parentFolderGuid = value) != null )
                    {
                        ParentFolder = Solution.FindProject( _parentFolderGuid ) as SolutionFolder;
                    }
                    else
                    {
                        ParentFolder = null;
                    }
                    Solution.SetDirtyStructure( true );
                }
            }
        }

        /// <summary>
        /// Gets or sets the parent folder.
        /// </summary>
        public SolutionFolder? ParentFolder
        {
            get => _parentFolder;
            set
            {
                if( _parentFolder != value )
                {
                    _parentFolder = value;
                    if( _parentFolder != null ) _parentFolderGuid = _parentFolder.ProjectGuid;
                    Solution.SetDirtyStructure( true );
                }
            }
        }

        /// <summary>
        /// Gets the full project name based on its <see cref="ParentFolder"/>'s path
        /// and <see cref="ProjectName"/>.
        /// This SHOULD be the same as <see cref="SolutionRelativeFolderPath"/> and should be enforced. 
        /// </summary>
        public NormalizedPath SolutionRelativeLogicalFolderPath => ParentFolder != null
                                                                        ? ParentFolder.SolutionRelativeLogicalFolderPath.AppendPart( ProjectName )
                                                                        : new NormalizedPath( ProjectName );

        internal virtual bool Initialize(
            FileSystem fs,
            IActivityMonitor m,
            Dictionary<NormalizedPath, MSProjFile> cache )
        {
            if( ParentFolderGuid != null
                && (ParentFolder = Solution.FindProjectByGuid<SolutionFolder>( m, ParentFolderGuid )) == null )
            {
                return false;
            }
            return true;
        }
    }
}
