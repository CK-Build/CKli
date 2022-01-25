using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Captures a set of modifications in a repository between 2 commits.
    /// </summary>
    public sealed class DiffResult
    {
        /// <summary>
        /// Initializes a new <see cref="DiffResult"/>.
        /// </summary>
        /// <param name="diffs">The list of defined rooted results.</param>
        /// <param name="others">Other changes.</param>
        public DiffResult( IReadOnlyList<DiffRootResult> diffs, DiffRootResult others )
        {
            Diffs = diffs;
            Others = others;
            ChangeCount = diffs.Sum( d => d.ChangeCount ) + others.ChangeCount;
        }

        /// <summary>
        /// Gets the total number of changes.
        /// </summary>
        public int ChangeCount { get; }

        /// <summary>
        /// Gets a list of <see cref="DiffRootResult"/> for roots that have
        /// been declared by <see cref="DiffRoot"/>.
        /// </summary>
        public IReadOnlyList<DiffRootResult> Diffs { get; }

        /// <summary>
        /// Gets <see cref="DiffRootResult"/> of modifications that have
        /// not been captured by <see cref="Diffs"/>.
        /// </summary>
        public DiffRootResult Others { get; }

        /// <summary>
        /// Overridden to return the details of the changes or "(no change)".
        /// Always ends with a new line.
        /// </summary>
        /// <returns>A readable string with a trailing new line.</returns>
        public override string ToString()
        {
            if( ChangeCount == 0 )
            {
                return "(no change)" + Environment.NewLine;
            }
            var sb = new StringBuilder();
            sb.AppendLine( $"{ChangeCount} changes:" );
            foreach( var r in Diffs )
            {
                r.ToString( sb );
            }
            Others.ToString( sb );
            return sb.ToString();
        }
    }
}
