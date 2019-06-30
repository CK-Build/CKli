using CK.Core;
using CK.Text;

namespace CK.Env.Diff
{
    class ModifiedDiff : IDiff, IModifiedDiff
    {
        public ModifiedDiff( NormalizedPath oldPath, NormalizedPath newPath)
        {
            OldPath = oldPath;
            NewPath = newPath;
        }

        /// <summary>
        /// Old path of the modified file.
        /// </summary>
        public NormalizedPath OldPath { get; }

        /// <summary>
        /// New path of the modified file.
        /// </summary>
        public NormalizedPath NewPath { get; }

        public bool SendToBuilder( IActivityMonitor m, DiffRootResultBuilderBase diffRootResultBuilder )
        {
            return diffRootResultBuilder.Accept(m, this );
        }
    }
}
