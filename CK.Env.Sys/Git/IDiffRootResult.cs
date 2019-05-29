using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Captures the 
    /// </summary>
    public interface IDiffRootResult
    {
        IDiffRoot Definition { get; }

        /// <summary>
        /// Gets the global change type that occured in the folder.
        /// </summary>
        DiffRootResultType DiffType { get; }


    }
}
