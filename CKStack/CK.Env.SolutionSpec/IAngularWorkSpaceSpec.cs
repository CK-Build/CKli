using CK.Core;

namespace CK.Env
{
    public interface IAngularWorkspaceSpec
    {
        NormalizedPath Path { get; }
        NormalizedPath OutputFolder { get; }
    }
}
