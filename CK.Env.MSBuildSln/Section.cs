using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CK.Env.MSBuildSln
{
    public class Section
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

        public string SectionType => _container is SolutionFile ? "GlobalSection" : "ProjectSection";

        public string Step { get; set; }

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
