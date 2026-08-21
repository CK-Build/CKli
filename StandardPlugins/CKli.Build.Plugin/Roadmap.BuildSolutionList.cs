using CKli.Core;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Lists all the <see cref="BuildSolution"/> by the <see cref="Repo.Index"/>.
    /// </summary>
    public sealed class BuildSolutionList : IReadOnlyList<BuildSolution>
    {
        readonly Roadmap _map;
        readonly IEnumerable<BuildSolution> _e;

        internal BuildSolutionList( Roadmap map )
        {
            _map = map;
            _e = _map._graph.Solutions.Select( s => _map._orderedSolutions[s.OrderedIndex] );
        }

        /// <summary>
        /// Gets the <see cref="BuildSolution"/> by its <see cref="BuildSolution.Repo"/>.
        /// </summary>
        /// <param name="repo">The repo.</param>
        /// <returns>The build solution.</returns>
        public BuildSolution this[Repo repo] => _map._orderedSolutions[_map._graph.Solutions[repo.Index].OrderedIndex];

        /// <summary>
        /// Gets the <see cref="BuildSolution"/> by its <see cref="Repo.Index"/>.
        /// </summary>
        /// <param name="index">The repository index in the world.</param>
        /// <returns>The build solution.</returns>
        public BuildSolution this[int index] => _map._orderedSolutions[_map._graph.Solutions[index].OrderedIndex];

        /// <inheritdoc />
        public int Count => _map._orderedSolutions.Length;

        /// <inheritdoc />
        public IEnumerator<BuildSolution> GetEnumerator() => _e.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
