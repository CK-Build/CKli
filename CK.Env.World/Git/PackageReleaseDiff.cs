using System;
using System.Collections.Generic;
using System.Text;

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
        /// <param name="p">The package.</param>
        /// <param name="c">The change.</param>
        public PackageReleaseDiff( GeneratedPackage p, PackageReleaseDiffType c )
        {
            Package = p;
            DiffType = c;
            Changes = Array.Empty<FileReleaseDiff>();
        }

        /// <summary>
        /// Initializes an new <see cref="PackageReleaseDiff"/> with a change set.
        /// </summary>
        /// <param name="p">The package.</param>
        /// <param name="changes">The changes.</param>
        public PackageReleaseDiff( GeneratedPackage p, IReadOnlyCollection<FileReleaseDiff> changes )
        {
            Package = p;
            DiffType = changes.Count == 0 ? PackageReleaseDiffType.None : PackageReleaseDiffType.Changed;
            Changes = changes;
        }

        /// <summary>
        /// Gets the package.
        /// </summary>
        public GeneratedPackage Package { get; }

        /// <summary>
        /// Gets the global change type that occured in
        /// the <see cref="GeneratedPackage.PrimarySolutionRelativeFolderPath"/>.
        /// </summary>
        public PackageReleaseDiffType DiffType { get; }

        /// <summary>
        /// Gets the file change set.
        /// Never null.
        /// </summary>
        public IReadOnlyCollection<FileReleaseDiff> Changes { get; }


    }
}
