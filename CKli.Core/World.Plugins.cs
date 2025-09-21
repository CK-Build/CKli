using CK.Core;
using CSemVer;
using System;

namespace CKli.Core;

public sealed partial class World
{
    internal bool AddPlugin( IActivityMonitor monitor, string packageId, SVersion version )
    {
        Throw.CheckState( !DefinitionFile.IsPluginsDisabled );
        monitor.Error( "Not implemented yet." );
        return false;
    }
}
