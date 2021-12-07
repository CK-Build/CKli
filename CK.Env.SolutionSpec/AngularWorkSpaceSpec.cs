using CK.Core;
using System.Xml.Linq;

namespace CK.Env
{
    public class AngularWorkspaceSpec : IAngularWorkspaceSpec
    {
        internal AngularWorkspaceSpec( XElementReader r )
        {
            Path = r.HandleRequiredAttribute<NormalizedPath>( nameof( Path ) );
            OutputFolder = r.HandleRequiredAttribute<NormalizedPath>( nameof( OutputFolder ) );
        }
        public NormalizedPath Path { get; }
        public NormalizedPath OutputFolder { get; }
    }
}
