
using System;

namespace CK.Env.MSBuildSln
{
    /// <summary>
    /// Logical NOT (!).
    /// </summary>
    public sealed class NotNode : BaseNode
    {
        public NotNode( BaseNode left )
        {
            Left = left ?? throw new ArgumentNullException( nameof( left ) );
        }

        public BaseNode Left { get; }

        public override string ToString() => $"Not ({Left})";

    }
}
