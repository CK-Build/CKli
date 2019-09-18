using CK.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public class AngularWorkspaceSpec : IAngularWorkspaceSpec
    {
        internal AngularWorkspaceSpec( XElementReader r)
        {
            Path = r.HandleRequiredAttribute<NormalizedPath>( nameof( Path ) );
        }
        public NormalizedPath Path { get; }
    }
}
