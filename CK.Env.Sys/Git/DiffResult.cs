using CK.Core;
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
        public DiffResult( IReadOnlyList<DiffRootResult> diffs, DiffRootResult others, IReadOnlyList<CommitMessage>? messages )
        {
            Diffs = diffs;
            Others = others;
            Messages = messages;
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
        /// Gets the list of commit messages if it has been computed.
        /// </summary>
        public IReadOnlyList<CommitMessage>? Messages { get; }

        /// <summary>
        /// Overridden to return the details of the changes or "(no change)".
        /// Always ends with a new line.
        /// </summary>
        /// <param name="withMessages">True to return the messages.</param>
        /// <param name="withDetails">True to returns the <see cref="Diffs"/> results.</param>
        /// <returns>A readable string with a trailing new line.</returns>
        public string ToString( bool withMessages, bool withDetails )
        {
            if( ChangeCount == 0 )
            {
                return "(no change)" + Environment.NewLine;
            }
            var sb = new StringBuilder();
            if( withMessages && Messages != null )
            {
                sb.AppendLine( $"{Messages.Count} Messages:" );
                foreach( var message in Messages )
                {
                    sb.Append( $"  > {message.CommitDate} " )
                      .AppendMultiLine( "                             | ", message.Message, false )
                      .AppendLine();
                }
            }
            if( withDetails )
            {
                sb.AppendLine( $"{ChangeCount} changes:" );
                foreach( var r in Diffs )
                {
                    r.ToString( sb );
                }
                Others.ToString( sb );
            }
            else
            {
                sb.AppendLine( $"({ChangeCount} file changes.)" );
            }
            return sb.ToString();
        }

        /// <summary>
        /// Overridden to return the details of the changes or "(no change)" without <see cref="Messages"/>.
        /// Always ends with a new line.
        /// </summary>
        /// <returns>A readable string with a trailing new line.</returns>
        public override string ToString() => ToString( false, true );
    }
}
