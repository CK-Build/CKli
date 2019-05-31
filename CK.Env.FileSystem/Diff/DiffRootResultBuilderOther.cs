using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.Diff
{
    /// <summary>
    /// Accept any changes
    /// </summary>
    class DiffRootResultBuilderOther : DiffRootResultBuilderBase
    {
        public DiffRootResultBuilderOther( IDiffRoot diffRoot ) : base( diffRoot )
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
