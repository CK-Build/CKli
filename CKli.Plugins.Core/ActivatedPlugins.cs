using System;

namespace CKli.Core;

/// <summary>
/// Contains actual plugins instances bound to a world.
/// This only dispatches the Dispose call to any IDisposable objects.
/// </summary>
public sealed class ActivatedPlugins : IDisposable
{
    object[]? _instantiated;

    /// <summary>
    /// Initializes this ActivatedPlugins with the plugin instances.
    /// </summary>
    /// <param name="instantiated">The plugin instances.</param>
    public ActivatedPlugins( object[] instantiated )
    {
        _instantiated = instantiated;
    }

    void IDisposable.Dispose()
    {
        if( _instantiated != null )
        {
            foreach( var o in _instantiated )
            {
                if( o is IDisposable d ) d.Dispose();
            }
            _instantiated = null;
        }
    }
}


