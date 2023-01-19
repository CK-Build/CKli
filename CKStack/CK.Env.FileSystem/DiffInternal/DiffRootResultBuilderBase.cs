using CK.Core;
using System.Collections.Generic;

namespace CK.Env.Diff
{
    abstract class DiffRootResultBuilderBase
    {
        protected Env.GitDiffRoot DiffRoot { get; }
        protected List<AddedDiff> AddedDiffs { get; } = new List<AddedDiff>();
        protected List<DeletedDiff> DeletedDiffs { get; } = new List<DeletedDiff>();
        protected List<ModifiedDiff> ModifiedDiffs { get; } = new List<ModifiedDiff>();
        public DiffRootResultBuilderBase( Env.GitDiffRoot diffRoot )
        {
            DiffRoot = diffRoot;
        }

        public abstract bool Accept( IActivityMonitor m, AddedDiff createdDiff );

        public abstract bool Accept( IActivityMonitor m, DeletedDiff deletedDiff );

        public abstract bool Accept( IActivityMonitor m, ModifiedDiff modifiedDiff );

        public GitDiffRootResult Result
        {
            get => new GitDiffRootResult( DiffRoot, AddedDiffs, DeletedDiffs, ModifiedDiffs );
        }
    }
}
