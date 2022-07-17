using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Captures commit info.
    /// </summary>
    public sealed class CommitMessage
    {
        /// <summary>
        /// Gets the commit identifier.
        /// </summary>
        public string CommitSha { get; }

        /// <summary>
        /// Gets the commit date.
        /// </summary>
        public DateTimeOffset CommitDate { get; }

        /// <summary>
        /// Gets the committer's name.
        /// </summary>
        public string CommitterName { get; }

        /// <summary>
        /// Gets the commit message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Appends the date and the message (one or more lines).
        /// </summary>
        /// <param name="b">The builder.</param>
        /// <returns>The builder.</returns>
        public StringBuilder ToString( StringBuilder b )
        {
            return b.Append( $"  > {CommitDate} " )
              .AppendMultiLine( "                             | ", Message, false )
              .AppendLine();
        }

        /// <summary>
        /// Initializes a new <see cref="CommitMessage"/>.
        /// </summary>
        /// <param name="sha">The commit identifier.</param>
        /// <param name="date">The commit date.</param>
        /// <param name="name">The committer's name.</param>
        /// <param name="message">The commit message.</param>
        public CommitMessage( string sha, DateTimeOffset date, string name, string message )
        {
            CommitSha = sha;
            CommitDate = date;
            CommitterName = name;
            Message = message;
        }
    }
}
