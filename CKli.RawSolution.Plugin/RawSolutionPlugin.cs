using CKli.Core;

namespace CKli.RawSolution.Plugin;

public static class Plugin
{
    public static void Register( WorldServiceCollection services )
    {
        services.Add<RawSolutionProvider>();
    }
}
