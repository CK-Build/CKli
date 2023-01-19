using CK.Core;

namespace CK.Env.Diff
{
    /// <summary>
    /// Accept any changes.
    /// </summary>
    sealed class DiffRootResultBuilderOther : DiffRootResultBuilderBase
    {
        public DiffRootResultBuilderOther( GitDiffRoot diffRoot )
            : base( diffRoot )
        {

        }

        public override bool Accept( IActivityMonitor m, AddedDiff createdDiff )
        {
            AddedDiffs.Add( createdDiff );
            return true;
        }

        public override bool Accept( IActivityMonitor m, DeletedDiff deletedDiff )
        {
            DeletedDiffs.Add( deletedDiff );
            return true;
        }

        public override bool Accept( IActivityMonitor m, ModifiedDiff modifiedDiff )
        {
            ModifiedDiffs.Add( modifiedDiff );
            return true;
        }
    }
}
