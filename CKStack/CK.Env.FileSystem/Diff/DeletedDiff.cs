using CK.Core;


namespace CK.Env.Diff
{
    class DeletedDiff : IDiff, IDeletedDiff
    {
        public DeletedDiff( NormalizedPath path )
        {
            Path = path;
        }

        /// <summary>
        /// Path of the deleted file.
        /// </summary>
        public NormalizedPath Path { get; }

        public bool SendToBuilder( IActivityMonitor m, DiffRootResultBuilderBase diffRootResultBuilder )
        {
            return diffRootResultBuilder.Accept( m, this );
        }
    }
}
