using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

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
        /// <param name="projectGuid">The folder identifier.</param>
        /// <param name="path">The folder (logical) path.</param>
        internal SolutionFolder( SolutionFile s, string projectGuid, NormalizedPath path )
            : base( s, projectGuid, KnownProjectType.SolutionFolder.ToGuid(), path.LastPart, path )
        {
            Debug.Assert( ProjectName == Path.LastPart, "The name ends the path for SolutionFolder." );
            _items = new List<NormalizedPath>();
        }

        public override string ProjectName
        {
            get => base.ProjectName;
            set
            {
                Path = Path.RemoveLastPart().AppendPart( value );
                base.ProjectName = value;
            }
        }

        /// <summary>
        /// Overridden to intercept section(SolutionItems): when the section's name is SolutionItems
        /// we call EnsureItem on its content.
        /// </summary>
        /// <param name="section">The section to add.</param>
        internal protected override void AddSection( Section section )
        {
            if( section.Name == "SolutionItems" )
            {
                foreach( var p in section.PropertyLines ) EnsureItem( p.Name );
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

        /// <summary>
        /// Ensures that a file appears in the folder.
        /// Case difference is updated.
        /// </summary>
        /// <param name="item">The path that must be relative to the solution folder.</param>
        /// <returns>True when added or updated, false if nothing changed.</returns>
        public bool EnsureItem( NormalizedPath item )
        {
            int idx = _items.IndexOf( i => String.Compare( i.Path, item.Path, ignoreCase: true, CultureInfo.InvariantCulture ) == 0 );
            if( idx >= 0 )
            {
                if( _items[idx] != item )
                {
                    _items[idx] = item;
                    Solution.SetDirtyStructure( true );
                    return true;
                }
            }
            else
            {
                _items.Add( item );
                Solution.SetDirtyStructure( true );
                return true;
            }
            return false;
        }

        public bool RemoveItem( NormalizedPath item )
        {
            int idx = _items.IndexOf( i => String.Compare( i.Path, item.Path, ignoreCase: true, CultureInfo.InvariantCulture ) == 0 );
            if( idx >= 0 )
            {
                _items.RemoveAt( idx );
                Solution.SetDirtyStructure( true );
                return true;
            }
            return false;
        }

        public override string ToString() => $"Folder '{SolutionRelativeLogicalFolderPath}'";

    }
}
