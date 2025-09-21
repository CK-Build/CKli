using CKli.Core;

namespace CKli.Plugins;

public static class Plugins
{
    public static IWorldPlugins Register( PluginCollectorContext ctx )
    {
        var collector = PluginCollector.Create( ctx );
        return collector.Build();
    }
}
