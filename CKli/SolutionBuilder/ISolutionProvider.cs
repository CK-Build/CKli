using CK.Core;
using CK.Env;
using CK.Env.DependencyModel;

namespace CKli
{
    public interface ISolutionProvider
    {
        bool Configure( IActivityMonitor monitor, GitRepository r, NormalizedPath branchPath, Solution s );

        bool Localize( IActivityMonitor monitor, SolutionContext solutions );
    }
}

