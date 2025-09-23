using CK.Core;

namespace CKli.Core;

public sealed class PluginInfoEvent : WorldEvent
{
    internal PluginInfoEvent( IActivityMonitor monitor, World world )
        : base( monitor, world )
    {
    }
}
