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
        string _projectName;
        SolutionFolder? _parentFolder;
        NormalizedPath _solutionRelativePath;

        internal ProjectBase( SolutionFile solution,
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
            _solutionRelativePath = relativePath;
            _sections = new Dictionary<string, Section>( StringComparer.OrdinalIgnoreCase );
        }

        /// <summary>
        /// Gets the solution that owns this project.
        /// </summary>
        public SolutionFile Solution { get; }

        internal virtual string ProjectHeader => $@"Project(""{ProjectTypeGuid}"") = ""{ProjectName}"", ""{SolutionRelativePath}"", ""{ProjectGuid}""";

        /// <summary>
        /// Gets the project type identifier.
        /// </summary>
        internal protected string ProjectTypeGuid { get; }

        /// <summary>
        /// Gets or sets the project name: this is the name in the <see cref="Solution"/> file.
        /// It may not be synchronized with the actual project file name.
        /// <para>
        /// For Project it can be changed independently of the other path related properties (but SHOULD follow the
        /// file system structure).
        /// For SolutionFolder it is part of the SolutionRelativePath.
        /// </para>
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
        /// For Project, gets the path to the project file relative to the <see cref="SolutionFile.SolutionFolderPath"/> (the .csproj file).
        /// <para>
        /// For SolutionFolder, this is by design the <see cref="SolutionRelativeLogicalFolderPath"/>.
        /// </para>
        /// </summary>
        public virtual NormalizedPath SolutionRelativePath
        {
            get => _solutionRelativePath;
            private protected set
            {
                // This is only called by SolutionFolder.
                // It's useless here to dirty the structure.
                // This can change because a ProjectName or a ParentFolder has changed.
                // Both impact the structure.
                _solutionRelativePath = value;
            }
        }

        /// <summary>
        /// Gets the project Guid.
        /// </summary>
        public string ProjectGuid { get; }

        /// <summary>
        /// Gets this project known type.
        /// </summary>
        public KnownProjectType KnownType { get; }

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

        internal string? ParentFolderGuid { get; set; }

        /// <summary>
        /// Gets or sets the parent folder.
        /// </summary>
        public SolutionFolder? ParentFolder
        {
            get => _parentFolder;
        }

        void SetParentFolder( SolutionFolder? parent, bool initialize )
        {
            if( _parentFolder != parent )
            {
                _parentFolder = parent;
                ParentFolderGuid = parent?.ProjectGuid;
                if( !initialize )
                {
                    UpdateSolutionRelativePath();
                    Solution.SetDirtyStructure( true );
                }
            }
        }

        /// <summary>
        /// Does nothing here: SolutionFolder cannot be inside a Project.
        /// </summary>
        internal virtual void UpdateSolutionRelativePath()
        {
        }

        /// <summary>
        /// Gets the full project name based on its <see cref="ParentFolder"/>'s path
        /// and <see cref="ProjectName"/>.
        /// <para>
        /// For SolutionFolder, this is the real SolutionRelativeFolderPath.
        /// </para>
        /// <para>
        /// For Project, this SHOULD be the same as <see cref="SolutionRelativeFolderPath"/> and should be enforced
        /// (at least by emitting warnings if they differ). 
        /// </para>
        /// </summary>
        public NormalizedPath SolutionRelativeLogicalFolderPath => ParentFolder != null
                                                                        ? ParentFolder.SolutionRelativeLogicalFolderPath.AppendPart( ProjectName )
                                                                        : new NormalizedPath( ProjectName );

        internal virtual bool Initialize( FileSystem fs,
                                          IActivityMonitor m,
                                          Dictionary<NormalizedPath, MSProjFile> cache )
        {
            if( ParentFolderGuid != null )
            {
                var p = Solution.FindProjectByGuid<SolutionFolder>( m, ParentFolderGuid );
                if( p == null )
                {
                    m.Error( $"Invalid nesting: '{ProjectGuid}''s parent '{ParentFolderGuid}' cannot be found." );
                    return false;
                }
                SetParentFolder( p, true );
            }
            return true;
        }
    }
}
