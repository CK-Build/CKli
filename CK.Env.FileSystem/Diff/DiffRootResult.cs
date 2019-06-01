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

        public override string ToString()
        {
            var sb = new StringBuilder();
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
                    sb.AppendLine( $"=    =    =>Added({AddedDiffs.Count}):" );
                    foreach( var f in AddedDiffs )
                    {
                        sb.Append( $"=    =    =|" )
                            .AppendLine( f.Path );
                    }
                }

                if( DeletedDiffs.Count == 0 )
                {
                    sb.AppendLine( "=    =    => No deleted files." );
                }
                else
                {
                    sb.AppendLine( $"=    =    =>Deleted({DeletedDiffs.Count}):" );
                    foreach( var f in DeletedDiffs )
                    {
                        sb.Append( $"=    =    =|" )
                            .AppendLine( f.Path );
                    }
                }
                if( ModifiedDiffs.Count == 0 )
                {
                    sb.AppendLine( "=    =    => No deleted files." );
                }
                else
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
