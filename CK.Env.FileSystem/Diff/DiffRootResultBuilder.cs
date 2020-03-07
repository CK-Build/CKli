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
                m.Debug( $"Added file ({createdDiff.Path}) matched with one of the DiffRoot '{DiffRoot.Name}' path: {matchingPath}" );
                AddedDiffs.Add( createdDiff );
                return true;
            }
            m.Debug( $"Added file ({createdDiff.Path}) did not match with any of the DiffRoot '{DiffRoot.Name}' paths." );
            return false;
        }

        public override bool Accept( IActivityMonitor m, DeletedDiff deletedDiff )
        {
            var matchingPath = DiffRoot.Paths.FirstOrDefault( path => deletedDiff.Path.StartsWith( path ) );
            if( matchingPath != null )
            {
                m.Debug( $"Deleted file ({deletedDiff.Path}) matched with one of the DiffRoot '{DiffRoot.Name}' path: {matchingPath}" );
                DeletedDiffs.Add( deletedDiff );
                return true;
            }
            m.Debug( $"Deleted file ({deletedDiff.Path}) did not match with any of the DiffRoot '{DiffRoot.Name}' paths." );
            return false;
        }

        public override bool Accept( IActivityMonitor m, ModifiedDiff modifiedDiff )
        {
            var matchingPath = DiffRoot.Paths.FirstOrDefault( path => modifiedDiff.OldPath.StartsWith( path ) || modifiedDiff.NewPath.StartsWith( path ) );
            if( matchingPath != null )
            {
                m.Debug( $"Modified file ('{modifiedDiff.OldPath}'=>'{modifiedDiff.NewPath}') matched with one of the DiffRoot '{DiffRoot.Name}' path: {matchingPath}" );
                ModifiedDiffs.Add( modifiedDiff );
                return true;
            }
            m.Debug( $"Modified file ('{modifiedDiff.OldPath}'=>'{modifiedDiff.NewPath}') did not match with any of the DiffRoot '{DiffRoot.Name}' paths." );
            return false;
        }
    }
}
