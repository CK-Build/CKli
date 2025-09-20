using CKli.Core;

namespace CKli.Plugin;

public static class Plugin
{
    public static void Register( PluginCollector collector )
    {
        BasicDotNetSolution.Register( collector );
        DotNetSolution.Register( collector );
    }
}
