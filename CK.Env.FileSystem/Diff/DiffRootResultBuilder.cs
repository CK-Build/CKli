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
                m.Trace( $"Accepted added file diff \"{createdDiff.Path}\" because this builder watch \"{matchingPath}\"" );
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
                m.Trace( $"Accepted deleted file diff \"{deletedDiff.Path}\" because this builder watch \"{matchingPath}\"" );
                DeletedDiffs.Add( deletedDiff );
                return true;
            }
            return false;
        }

        public override bool Accept( IActivityMonitor m, ModifiedDiff modifiedDiff )
        {
            var matchingPath = DiffRoot.Paths.FirstOrDefault( path => modifiedDiff.OldPath.StartsWith( path ) || modifiedDiff.NewPath.StartsWith(path) );
            if( matchingPath != null )
            {
                if(modifiedDiff.OldPath != modifiedDiff.NewPath)
                {
                    m.Trace( $"Accepted modified file diff \"{modifiedDiff.OldPath}\"=>\"{modifiedDiff.NewPath}\" because this builder watch \"{matchingPath}\"" );
                }
                else
                {
                    m.Trace( $"Accepted modified file diff \"{modifiedDiff.NewPath}\" because this builder watch \"{matchingPath}\"" );
                }
                ModifiedDiffs.Add( modifiedDiff );
                return true;
            }
            return false;
        }
    }
}
