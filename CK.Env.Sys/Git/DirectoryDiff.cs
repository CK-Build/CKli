using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Captures the 
    /// </summary>
    public class DirectoryDiff
    {
        readonly IReadOnlyCollection<CommitInfo> _commits;

        /// <summary>
        /// Initializes an new <see cref="DirectoryDiff"/> with a no change set but a status.
        /// </summary>
        /// <param name="p">The package name.</param>
        /// <param name="c">The change.</param>
        public DirectoryDiff( NormalizedPath p, DirectoryDiffType c )
        {
            DiffName = p;
            DiffType = c;
            Changes = Array.Empty<FileReleaseDiff>();
        }

        /// <summary>
        /// Initializes an new <see cref="DirectoryDiff"/> with a change set.
        /// </summary>
        /// <param name="name">The package name.</param>
        /// <param name="changes">The changes.</param>
        public DirectoryDiff( string name, IReadOnlyCollection<FileReleaseDiff> changes, IReadOnlyCollection<CommitInfo> commits )
        {
            DiffName = name;
            DiffType = changes.Count == 0 ? DirectoryDiffType.None : DirectoryDiffType.Changed;
            Changes = changes;
            _commits = commits;
        }

        public void DumpDiff( IActivityMonitor m )
        {
            using( m.OpenInfo( $"=    => {DiffName}: {DiffType}" ) )
            {
                if( DiffType == DirectoryDiffType.Changed || DiffType == DirectoryDiffType.NewPackage )
                {
                    using( m.OpenInfo( "Commit with diffs that changed the path:" ) )
                    {
                        foreach( CommitInfo commit in _commits )
                        {
                            m.Info( $"{commit.Sha.Substring(0,5)}: {commit.Message}" );
                        }
                    }
                    using( m.OpenInfo( "Impacted files:" ) )
                    {

                        foreach( var fC in Changes.GroupBy( fC => fC.DiffType ) )
                        {
                            using( m.OpenInfo( fC.Key.ToString() ) )
                            {
                                foreach( var f in fC )
                                {
                                    m.Info( $"{f.FilePath}" );
                                }
                            }

                        }
                    }
                }
            }
        }

        /// <summary>
        /// Diff name
        /// </summary>
        public string DiffName { get; }

        /// <summary>
        /// Gets the global change type that occured in the folder.
        /// </summary>
        public DirectoryDiffType DiffType { get; }

        /// <summary>
        /// Gets the file change set.
        /// Never null.
        /// </summary>
        public IReadOnlyCollection<FileReleaseDiff> Changes { get; }


    }
}
