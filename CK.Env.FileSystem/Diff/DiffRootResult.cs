using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.Diff
{
    /// <summary>
    /// Captures the 
    /// </summary>
    public class DiffRootResult : IDiffRootResult
    {
        readonly IReadOnlyCollection<CommitInfo> _commits;

        /// <summary>
        /// Initializes an new <see cref="DiffRootResult"/> with a change set.
        /// </summary>
        /// <param name="name">The package name.</param>
        /// <param name="changes">The changes.</param>
        internal DiffRootResult( IDiffRoot definition, IReadOnlyCollection<IFileReleaseDiff> changes, IReadOnlyCollection<CommitInfo> commits )
        {
            Definition = definition;
            DiffType = changes.Count == 0 ? DiffRootResultType.None : DiffRootResultType.Changed;
            Changes = changes;
            _commits = commits;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine( $"=> {Definition}: {DiffType}" );
            if( DiffType == DiffRootResultType.Changed || DiffType == DiffRootResultType.NewPackage )
            {
                sb.AppendLine( "=    => Commit with diffs that changed the path:" );
                foreach( CommitInfo commit in _commits )
                {
                    sb.Append( "=    =    =|" )
                        .Append( commit.Sha.Substring( 0, 5 ) )
                        .Append( ':' )
                        .Append( commit.Message );
                }
                sb.AppendLine( "=    => Impacted files:" );
                foreach( var fC in Changes.GroupBy( fC => fC.DiffType ) )
                {
                    sb.Append( "=    =    =>" )
                        .AppendLine( fC.Key.ToString() );
                    foreach( var f in fC )
                    {
                        sb.Append( "=    =    =|" )
                            .AppendLine( f.FilePath );
                    }
                }
            }
            return sb.ToString();
        }

        public IDiffRoot Definition { get; }

        /// <summary>
        /// Gets the global change type that occured in the folder.
        /// </summary>
        public DiffRootResultType DiffType { get; }

        /// <summary>
        /// Gets the file change set.
        /// Never null.
        /// </summary>
        public IReadOnlyCollection<IFileReleaseDiff> Changes { get; }
    }
}
