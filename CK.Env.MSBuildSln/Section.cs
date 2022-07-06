using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// A section appears in a container and is identified by its <see cref="Name"/>.
    /// If the container is the <see cref="SolutionFile"/>, then the <see cref="SectionType"/> is a "GlobalSection",
    /// else it is a "ProjectSection".
    /// </summary>
    public sealed class Section
    {
        readonly ISolutionItem _container;
        readonly Dictionary<string, PropertyLine> _propertyLines;

        internal Section( ISolutionItem container, string name, string step )
            : this( container, name, step, new Dictionary<string, PropertyLine>( StringComparer.OrdinalIgnoreCase ) )
        {
        }

        internal Section( ISolutionItem container, string name, string step, Dictionary<string, PropertyLine> lines )
        {
            Debug.Assert( lines != null );
            _container = container;
            Name = name;
            Step = step;
            _propertyLines = lines;
        }

        /// <summary>
        /// Gets the name of this section. This name uniquely identifies a section.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets "GlobalSection" or "ProjectSection".
        /// Used to write the .sln file back.
        /// </summary>
        public string SectionType => _container is SolutionFile ? "GlobalSection" : "ProjectSection";

        /// <summary>
        /// Gets whether this is a global section, attached to the <see cref="SolutionFile"/>.
        /// </summary>
        public bool IsGlobalSection => _container is SolutionFile;

        /// <summary>
        /// Gets the "Step" of the section ("preProject", "preSolution", "postSolution", etc.).
        /// </summary>
        public string Step { get; }

        /// <summary>
        /// Gets the property lines of this section.
        /// </summary>
        public IReadOnlyCollection<PropertyLine> PropertyLines => _propertyLines.Values;

        /// <summary>
        /// Adds a line in the <see cref="PropertyLines"/>.
        /// </summary>
        /// <param name="p">The line to add.</param>
        public void AddPropertyLine( PropertyLine p )
        {
            _propertyLines.Add( p.Name, p );
            _container.Solution.SetDirtyStructure( true );
        }

        public override string ToString() => $"{SectionType} '{Name}'";
    }
}
