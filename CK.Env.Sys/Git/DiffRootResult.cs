using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Results for a <see cref="DiffRoot"/>.
    /// </summary>
    public sealed class DiffRootResult
    {
        /// <summary>
        /// Gets the root with its name and possibly multiple paths.
        /// </summary>
        public DiffRoot Root { get; }

        /// <summary>
        /// Gets the total number of changes.
        /// </summary>
        public int ChangeCount { get; }

        /// <summary>
        /// Gets the files that have been added.
        /// </summary>
        public IReadOnlyList<IAddedDiff> AddedDiffs { get; }

        /// <summary>
        /// Gets the files that have been deleted.
        /// </summary>
        public IReadOnlyList<IDeletedDiff> DeletedDiffs { get; }

        /// <summary>
        /// Gets the files that have been modified.
        /// </summary>
        public IReadOnlyList<IModifiedDiff> ModifiedDiffs { get; }

        /// <summary>
        /// Initializes an new <see cref="DiffRootResult"/> with a change set.
        /// </summary>
        /// <param name="root">The root.</param>
        /// <param name="addedDiffs">The new files.</param>
        /// <param name="deletedDiffs">The deleted files.</param>
        /// <param name="modifiedDiffs">The modified files.</param>
        public DiffRootResult( DiffRoot root,
                               IReadOnlyList<IAddedDiff> addedDiffs,
                               IReadOnlyList<IDeletedDiff> deletedDiffs,
                               IReadOnlyList<IModifiedDiff> modifiedDiffs )
        {
            Throw.CheckNotNullArgument( root );
            Throw.CheckNotNullArgument( addedDiffs );
            Throw.CheckNotNullArgument( deletedDiffs );
            Throw.CheckNotNullArgument( modifiedDiffs );
            Root = root;
            AddedDiffs = addedDiffs;
            DeletedDiffs = deletedDiffs;
            ModifiedDiffs = modifiedDiffs;
            ChangeCount = addedDiffs.Count + deletedDiffs.Count + modifiedDiffs.Count;
        }

        /// <summary>
        /// Initializes an new empty <see cref="DiffRootResult"/>.
        /// </summary>
        /// <param name="root">The root.</param>
        public DiffRootResult( DiffRoot root )
        {
            Throw.CheckNotNullArgument( root );
            Root = root;
            AddedDiffs = Array.Empty<IAddedDiff>();
            DeletedDiffs = Array.Empty<IDeletedDiff>();
            ModifiedDiffs = Array.Empty<IModifiedDiff>();
        }

        /// <summary>
        /// Fills the builder with the detailed results.
        /// </summary>
        /// <param name="b">The builder.</param>
        /// <returns>The builder.</returns>
        public StringBuilder ToString( StringBuilder b )
        {
            Throw.CheckNotNullArgument( b );
            if( ChangeCount == 0 )
            {
                b.AppendLine( $"=> {Root.Name} (no change)." );
            }
            else
            {
                b.AppendLine( $"=> {Root.Name} - {ChangeCount} changes: {AddedDiffs.Count} added, {ModifiedDiffs.Count} modified, {DeletedDiffs.Count} removed:" );
                if( AddedDiffs.Count > 0 )
                {
                    for( int i = 0; i < AddedDiffs.Count; ++i )
                    {
                        if( i == 0 )
                        {
                            b.Append( $"=    Added: " );
                        }
                        else
                        {
                            b.Append( $"=         : " );
                        }
                        b.AppendLine( AddedDiffs[i].Path );
                    }
                }
                if( ModifiedDiffs.Count > 0 )
                {
                    for( int i = 0; i < ModifiedDiffs.Count; ++i )
                    {
                        if( i == 0 )
                        {
                            b.Append( $"=    Modified: " );
                        }
                        else
                        {
                            b.Append( $"=            : " );
                        }
                        var f = ModifiedDiffs[i];
                        if( f.NewPath != f.OldPath )
                        {
                            b.Append( f.OldPath ).Append( " => " );
                        }
                        b.AppendLine( f.NewPath );
                    }
                }
                if( DeletedDiffs.Count > 0 )
                {
                    for( int i = 0; i < DeletedDiffs.Count; ++i )
                    {
                        if( i == 0 )
                        {
                            b.Append( $"=    Removed: " );
                        }
                        else
                        {
                            b.Append( $"=           : " );
                        }
                        b.AppendLine( DeletedDiffs[i].Path );
                    }
                }
            }
            return b;
        }

        /// <summary>
        /// Overridden to return the details of the changes.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => ToString( new StringBuilder() ).ToString();

    }
}
