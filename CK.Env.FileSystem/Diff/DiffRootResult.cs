using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Diff
{
    /// <summary>
    /// Object representation of multiples diffs.
    /// </summary>
    public sealed class DiffRootResult : IDiffRootResult
    {
        public IReadOnlyList<IAddedDiff> AddedDiffs { get; }

        public IReadOnlyList<IDeletedDiff> DeletedDiffs { get; }

        public IReadOnlyList<IModifiedDiff> ModifiedDiffs { get; }

        /// <summary>
        /// Initializes an new <see cref="DiffRootResult"/> with a change set.
        /// </summary>
        /// <param name="name">The package name.</param>
        /// <param name="changes">The changes.</param>
        internal DiffRootResult( IDiffRoot definition, IReadOnlyList<AddedDiff> addedDiffs, IReadOnlyList<DeletedDiff> deletedDiffs, IReadOnlyList<ModifiedDiff> modifiedDiffs )
        {
            Definition = definition;
            AddedDiffs = addedDiffs;
            DeletedDiffs = deletedDiffs;
            ModifiedDiffs = modifiedDiffs;
            DiffType = addedDiffs.Count + deletedDiffs.Count + modifiedDiffs.Count == 0 ? DiffRootResultType.None : DiffRootResultType.Changed;
        }

        /// <summary>
        /// Initializes an new <see cref="DiffRootResult"/> empty.
        /// </summary>
        /// <param name="name">The package name.</param>
        internal DiffRootResult( IDiffRoot definition )
        {
            Definition = definition;
            AddedDiffs = Array.Empty<AddedDiff>();
            DeletedDiffs = Array.Empty<DeletedDiff>();
            ModifiedDiffs = Array.Empty<ModifiedDiff>();
            DiffType = DiffRootResultType.None;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            void DisplayAdded()
            {
                sb.AppendLine( $"=    =    => Added({AddedDiffs.Count}):" );
                foreach( var f in AddedDiffs )
                {
                    sb.Append( $"=    =    =|" )
                        .AppendLine( f.Path );
                }
            }
            void DisplayDeleted()
            {
                sb.AppendLine( $"=    =    =>Deleted({DeletedDiffs.Count}):" );
                foreach( var f in DeletedDiffs )
                {
                    sb.Append( $"=    =    =|" ).AppendLine( f.Path );
                }
            }
            void DisplayModified()
            {
                sb.AppendLine( $"=    =    =>Modified({ModifiedDiffs.Count}):" );
                foreach( var f in ModifiedDiffs )
                {
                    sb.Append( $"=    =    =|" );
                    if( f.NewPath != f.OldPath )
                    {
                        sb.Append( f.OldPath )
                          .Append( "=>" );
                    }
                    sb.AppendLine( f.NewPath );
                }
            }
            sb.AppendLine( $"=> {Definition.Name}: {DiffType}" );
            if( DiffType == DiffRootResultType.Changed || DiffType == DiffRootResultType.NewPackage )
            {
                sb.AppendLine( "=    => Impacted files:" );
                if( AddedDiffs.Count == 0 )
                {
                    sb.AppendLine( "=    =    => No added files." );
                }
                else
                {
                    DisplayAdded();
                }

                if( DeletedDiffs.Count == 0 )
                {
                    sb.AppendLine( "=    =    => No deleted files." );
                }
                else
                {
                    DisplayDeleted();
                }
                if( ModifiedDiffs.Count == 0 )
                {
                    sb.AppendLine( "=    =    => No deleted files." );
                }
                else
                {
                    DisplayModified();
                }
            }
            return sb.ToString();
        }

        public IDiffRoot Definition { get; }

        /// <summary>
        /// Gets the global change type that occured in the folder.
        /// </summary>
        public DiffRootResultType DiffType { get; }

    }
}
