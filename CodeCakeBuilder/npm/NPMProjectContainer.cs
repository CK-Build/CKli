using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeCake
{
    /// <summary>
    /// Contain multiple projects.
    /// </summary>
    public class NPMProjectContainer
    {
        readonly List<YarnWorkspace> _projects;
        readonly List<NPMProjectContainer> _containers;

        /// <summary>
        /// Object reprensenting a sets of NPMProjects, that may contain one or multiple projects containers..
        /// </summary>
        public NPMProjectContainer()
        {
            _projects = new List<YarnWorkspace>();
            _containers = new List<NPMProjectContainer>();
        }

        /// <summary>
        /// Gets the projects of this container.
        /// </summary>
        public IReadOnlyList<YarnWorkspace> SimpleProjects => _projects;

        /// <summary>
        /// Gets the projects of this container that can be published.
        /// </summary>
        public IEnumerable<NPMPublishedProject> SimplePublishedProjects => SimpleProjects.OfType<NPMPublishedProject>();

        /// <summary>
        /// Gets the Container stored in this container.
        /// </summary>
        public IReadOnlyList<NPMProjectContainer> Containers => _containers;

        /// <summary>
        /// Return All the projects, including All the projects of the <see cref="Containers"/>.
        /// </summary>
        public IEnumerable<YarnWorkspace> AllProjects => SimpleProjects.Concat( Containers.SelectMany( s => s.AllProjects ) );

        /// <summary>
        /// Return All the <see cref="YarnWorkspace"/> that are <see cref="NPMPublishedProject"/>, including All the projects that can be published in the <see cref="Containers"/>.
        /// </summary>
        public IEnumerable<NPMPublishedProject> AllPublishedProjects => SimplePublishedProjects.Concat( Containers.SelectMany( s => s.AllPublishedProjects ) );

        public void Add( YarnWorkspace project )
        {
            if( _projects.Contains( project ) ) throw new InvalidOperationException( "Element was already present in the list." );
            _projects.Add( project );
        }

        public void Add( NPMProjectContainer container )
        {
            if( _containers.Contains( container ) ) throw new InvalidOperationException( "Element was already present in the list." );
            _containers.Add( container );
        }
    }
}
