using CK.Core;
using System.Linq;

namespace CK.Env.Diff
{
    sealed class DiffRootResultBuilder : DiffRootResultBuilderBase
    {
        public DiffRootResultBuilder( GitDiffRoot diffRoot )
            : base( diffRoot )
        {
        }

        public override bool Accept( IActivityMonitor monitor, AddedDiff d )
        {
            var matchingPath = DiffRoot.Paths.FirstOrDefault( path => d.Path.StartsWith( path ) );
            if( matchingPath != null )
            {
                monitor.Debug( $"Added file ({d.Path}) matched with one of the DiffRoot '{DiffRoot.Name}' path: {matchingPath}" );
                AddedDiffs.Add( d );
                return true;
            }
            monitor.Debug( $"Added file ({d.Path}) did not match with any of the DiffRoot '{DiffRoot.Name}' paths." );
            return false;
        }

        public override bool Accept( IActivityMonitor monitor, DeletedDiff d )
        {
            var matchingPath = DiffRoot.Paths.FirstOrDefault( path => d.Path.StartsWith( path ) );
            if( matchingPath != null )
            {
                monitor.Debug( $"Deleted file ({d.Path}) matched with one of the DiffRoot '{DiffRoot.Name}' path: {matchingPath}" );
                DeletedDiffs.Add( d );
                return true;
            }
            monitor.Debug( $"Deleted file ({d.Path}) did not match with any of the DiffRoot '{DiffRoot.Name}' paths." );
            return false;
        }

        public override bool Accept( IActivityMonitor monitor, ModifiedDiff d )
        {
            var matchingPath = DiffRoot.Paths.FirstOrDefault( path => d.OldPath.StartsWith( path ) || d.NewPath.StartsWith( path ) );
            if( matchingPath != null )
            {
                monitor.Debug( $"Modified file ('{d.OldPath}'=>'{d.NewPath}') matched with one of the DiffRoot '{DiffRoot.Name}' path: {matchingPath}" );
                ModifiedDiffs.Add( d );
                return true;
            }
            monitor.Debug( $"Modified file ('{d.OldPath}'=>'{d.NewPath}') did not match with any of the DiffRoot '{DiffRoot.Name}' paths." );
            return false;
        }
    }
}
