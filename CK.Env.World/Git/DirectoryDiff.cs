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
        readonly IReadOnlyCollection<(string commitSha, string logMessage)> _commits;

        /// <summary>
        /// Initializes an new <see cref="DirectoryDiff"/> with a no change set but a status.
        /// </summary>
        /// <param name="p">The package name.</param>
        /// <param name="c">The change.</param>
        public DirectoryDiff( NormalizedPath p, DirectoryDiffType c )
        {
            Path = p;
            DiffType = c;
            Changes = Array.Empty<FileReleaseDiff>();
        }

        /// <summary>
        /// Initializes an new <see cref="DirectoryDiff"/> with a change set.
        /// </summary>
        /// <param name="p">The package name.</param>
        /// <param name="changes">The changes.</param>
        public DirectoryDiff( NormalizedPath p, IReadOnlyCollection<FileReleaseDiff> changes, IReadOnlyCollection<(string commitSha, string logMessage)> commits )
        {
            Path = p;
            DiffType = changes.Count == 0 ? DirectoryDiffType.None : DirectoryDiffType.Changed;
            Changes = changes;
            _commits = commits;
        }



        public void DumpDiff()
        {
            Console.WriteLine( $"=    => {Path}: {DiffType}" );
            foreach( (string commitSha, string logMessage) in _commits )
            {
                Console.Write( $"{commitSha}: {logMessage}" );
            }
            if( DiffType == DirectoryDiffType.Changed )
            {
                foreach( var fC in Changes.GroupBy( fC => fC.DiffType ) )
                {
                    Console.WriteLine( $"=       - {fC.Key}" );
                    foreach( var f in fC )
                    {
                        Console.WriteLine( $"               {f.FilePath}" );
                    }
                }
            }
        }

        /// <summary>
        /// Gets the package.
        /// </summary>
        public NormalizedPath Path { get; }

        /// <summary>
        /// Gets the global change type that occured in
        /// the <see cref="GeneratedArtifact.PrimarySolutionRelativeFolderPath"/>.
        /// </summary>
        public DirectoryDiffType DiffType { get; }

        /// <summary>
        /// Gets the file change set.
        /// Never null.
        /// </summary>
        public IReadOnlyCollection<FileReleaseDiff> Changes { get; }


    }
}
