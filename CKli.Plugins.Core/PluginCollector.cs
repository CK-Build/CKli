using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Plugins;

public static class PluginCollector
{
    public static IPluginCollector Create( PluginCollectorContext ctx )
    {
        return new ReflectionBasedPluginCollector( ctx );
    }
}
