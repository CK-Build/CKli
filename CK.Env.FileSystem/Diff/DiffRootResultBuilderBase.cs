using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.Diff
{
    abstract class DiffRootResultBuilderBase
    {
        protected  IDiffRoot DiffRoot { get; }
        protected  List<AddedDiff> AddedDiffs { get; } = new List<AddedDiff>();
        protected  List<DeletedDiff> DeletedDiffs { get; } = new List<DeletedDiff>();
        protected  List<ModifiedDiff> ModifiedDiffs { get; } = new List<ModifiedDiff>();
        public DiffRootResultBuilderBase( IDiffRoot diffRoot )
        {
            DiffRoot = diffRoot;
        }

        public abstract bool Accept( IActivityMonitor m, AddedDiff createdDiff );

        public abstract bool Accept( IActivityMonitor m, DeletedDiff deletedDiff );

        public abstract bool Accept( IActivityMonitor m, ModifiedDiff modifiedDiff );

        public DiffRootResult Result
        {
            get => new DiffRootResult( DiffRoot, AddedDiffs, DeletedDiffs, ModifiedDiffs );
        }
    }
}
