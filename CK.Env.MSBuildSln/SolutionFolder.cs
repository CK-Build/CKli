using CK.Text;
using System;
using System.Collections.Generic;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Solution folder.
    /// </summary>
    public class SolutionFolder : ProjectBase
    {
        readonly List<NormalizedPath> _items;

        /// <summary>
        /// Initializes a new <see cref="SolutionFolder"/>.
        /// </summary>
        /// <param name="s">The solution.</param>
        /// <param name="projectGuid">The forder identifier.</param>
        /// <param name="path">The folder (logical) path.</param>
        internal SolutionFolder( SolutionFile s, string projectGuid, NormalizedPath path )
            : base( s, projectGuid, KnownProjectType.SolutionFolder.ToGuid(), path.LastPart, path )
        {
            _items = new List<NormalizedPath>();
        }

        public override string ProjectName
        {
            get => base.ProjectName;
            set
            {
                base.ProjectName = value;
            }
        }

        /// <summary>
        /// Overridden to intercept SolutionItems.
        /// </summary>
        /// <param name="section">The section to add.</param>
        internal protected override void AddSection( Section section )
        {
            if( section.Name == "SolutionItems" )
            {
                foreach( var p in section.PropertyLines ) AddItem( p.Name );
            }
            else base.AddSection( section );
        }

        /// <summary>
        /// Gets the children of this folder.
        /// </summary>
        public IEnumerable<ProjectBase> Children
        {
            get
            {
                foreach( var project in Solution.AllProjects )
                {
                    if( project.ParentFolderGuid == ProjectGuid )
                        yield return project;
                }
            }
        }

        public IReadOnlyList<NormalizedPath> Items => _items;

        public void AddItem( NormalizedPath item )
        {
            if( _items.Contains( item ) )
            {
                throw new InvalidOperationException( $"Item {item} is already referenced." );
            }
            _items.Add( item );
            Solution.SetDirtyStructure( true );
        }

        public override string ToString() => $"Folder '{SolutionRelativeLogicalFolderPath}'";

    }
}
