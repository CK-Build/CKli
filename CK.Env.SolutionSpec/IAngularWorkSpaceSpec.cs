using CK.Text;

namespace CK.Env
{
    public interface IAngularWorkspaceSpec
    {
        NormalizedPath Path { get; }
        NormalizedPath OutputFolder { get; }
    }
}
