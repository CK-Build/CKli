
using System;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Logical NOT (!).
    /// </summary>
    public sealed class NotNode : BaseNode
    {
        public NotNode( BaseNode left )
        {
            if( left == null ) throw new ArgumentNullException( nameof( left ) );
            Left = left;
        }

        public BaseNode Left { get; }

        public override string ToString() => $"!({Left})";

    }
}
