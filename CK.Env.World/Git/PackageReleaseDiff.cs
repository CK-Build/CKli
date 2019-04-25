using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Captures the 
    /// </summary>
    public class PackageReleaseDiff
    {
        /// <summary>
        /// Initializes an new <see cref="PackageReleaseDiff"/> with a no change set but a status.
        /// </summary>
        /// <param name="p">The package name.</param>
        /// <param name="c">The change.</param>
        public PackageReleaseDiff( string p, PackageReleaseDiffType c )
        {
            Package = p;
            DiffType = c;
            Changes = Array.Empty<FileReleaseDiff>();
        }

        /// <summary>
        /// Initializes an new <see cref="PackageReleaseDiff"/> with a change set.
        /// </summary>
        /// <param name="p">The package name.</param>
        /// <param name="changes">The changes.</param>
        public PackageReleaseDiff( string p, IReadOnlyCollection<FileReleaseDiff> changes )
        {
            Package = p;
            DiffType = changes.Count == 0 ? PackageReleaseDiffType.None : PackageReleaseDiffType.Changed;
            Changes = changes;
        }



        public void DumpDiff()
        {
            Console.WriteLine( $"=    => {Package}: {DiffType}" );
            if( DiffType == PackageReleaseDiffType.Changed )
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
        public string Package { get; }

        /// <summary>
        /// Gets the global change type that occured in
        /// the <see cref="GeneratedArtifact.PrimarySolutionRelativeFolderPath"/>.
        /// </summary>
        public PackageReleaseDiffType DiffType { get; }

        /// <summary>
        /// Gets the file change set.
        /// Never null.
        /// </summary>
        public IReadOnlyCollection<FileReleaseDiff> Changes { get; }


    }
}
