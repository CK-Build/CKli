using System;
using System.Collections.Generic;

namespace CKli.Core;

public interface IWorldPlugins : IDisposable
{
    IReadOnlyCollection<IWorldPluginInfo> Plugins { get; }

    IDisposable Create( World world );
}

