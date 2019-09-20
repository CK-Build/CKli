using CK.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public class AngularWorkspaceSpec : IAngularWorkspaceSpec
    {
        internal AngularWorkspaceSpec( XElementReader r )
        {
            Path = r.HandleRequiredAttribute<NormalizedPath>( nameof( Path ) );
            OutputPath = r.HandleRequiredAttribute<NormalizedPath>( nameof( OutputPath ) );
        }
        public NormalizedPath Path { get; }
        public NormalizedPath OutputPath { get; }
    }
}
