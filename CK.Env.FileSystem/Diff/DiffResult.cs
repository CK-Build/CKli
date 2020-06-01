using System.Collections.Generic;
using System.Text;

namespace CK.Env.Diff
{
    public class DiffResult : IDiffResult
    {
        internal DiffResult( List<DiffRootResult> diffs, DiffRootResult others )
        {
            Diffs = diffs;
            Others = others;
        }

        public IReadOnlyList<IDiffRootResult> Diffs { get; }

        public IDiffRootResult Others { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if( Diffs.Count == 0 )
            {
                sb.AppendLine( "No diffs." );
            }
            sb.AppendLine( "Diffs detected: " );
            foreach( var diff in Diffs )
            {
                sb.Append( diff.ToString() );
            }
            sb.AppendLine( Others.ToString() );
            return sb.ToString();
        }
    }
}
