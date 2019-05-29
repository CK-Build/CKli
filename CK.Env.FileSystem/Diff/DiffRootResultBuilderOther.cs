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
            m.Trace( "This builder accepted the added diff." );
            AddedDiffs.Add( createdDiff );
            return true;
        }

        public override bool Accept( IActivityMonitor m, DeletedDiff deletedDiff )
        {
            m.Trace( "This builder accepted the added diff." );
            DeletedDiffs.Add( deletedDiff );
            return true;
        }

        public override bool Accept( IActivityMonitor m, ModifiedDiff modifiedDiff )
        {
            m.Trace( "This builder accepted the modified diff." );
            ModifiedDiffs.Add( modifiedDiff );
            return true;
        }
    }
}
