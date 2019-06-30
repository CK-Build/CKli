using CK.Core;
using System.Linq;

namespace CK.Env.Diff
{
    class DiffRootResultBuilder : DiffRootResultBuilderBase
    {
        public DiffRootResultBuilder( IDiffRoot diffRoot ) : base( diffRoot )
        {

        }

        public override bool Accept( IActivityMonitor m, AddedDiff createdDiff )
        {
            var matchingPath = DiffRoot.Paths.FirstOrDefault( path => createdDiff.Path.StartsWith( path ) );
            if( matchingPath != null )
            {
                AddedDiffs.Add( createdDiff );
                return true;
            }
            return false;
        }

        public override bool Accept( IActivityMonitor m, DeletedDiff deletedDiff )
        {
            var matchingPath = DiffRoot.Paths.FirstOrDefault( path => deletedDiff.Path.StartsWith( path ) );
            if( matchingPath != null )
            {
                DeletedDiffs.Add( deletedDiff );
                return true;
            }
            return false;
        }

        public override bool Accept( IActivityMonitor m, ModifiedDiff modifiedDiff )
        {
            if( DiffRoot.Paths.FirstOrDefault( path => modifiedDiff.OldPath.StartsWith( path ) || modifiedDiff.NewPath.StartsWith( path ) ) != null )
            {
                ModifiedDiffs.Add( modifiedDiff );
                return true;
            }
            return false;
        }
    }
}
