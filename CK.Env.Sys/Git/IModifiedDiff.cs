using CK.Text;
using System.Collections.Generic;

namespace CK.Env
{
    public interface IModifiedDiff
    {
        /// <summary>
        /// Old path of the modified file.
        /// </summary>
        NormalizedPath OldPath { get; }

        /// <summary>
        /// New path of the modified file.
        /// </summary>
        NormalizedPath NewPath { get; }

    }
}
