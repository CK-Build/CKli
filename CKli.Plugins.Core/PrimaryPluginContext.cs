using System.Xml.Linq;

namespace CKli.Core;

sealed class PrimaryPluginContext : IPrimaryPluginContext
{
    public PrimaryPluginContext( PluginInfo pluginInfo, XElement xmlConfiguration, World world )
    {
        PluginInfo = pluginInfo;
        XmlConfiguration = xmlConfiguration;
        World = world;
    }

    public PluginInfo PluginInfo { get; }

    public XElement XmlConfiguration { get; }

    public World World { get; }
}
