using CK.Core;
using CK.PerfectEvent;
using System;

namespace CKli.Core;

/// <summary>
/// Exposes the <see cref="World.Events"/> to which <see cref="PluginBase"/> can subscribe.
/// </summary>
public sealed class WorldEvents
{
    internal readonly PerfectEventSender<RepoAddedEvent> RepoAddedEventSender;

    internal WorldEvents()
    {
        RepoAddedEventSender = new PerfectEventSender<RepoAddedEvent>();
    }

    internal void ReleaseEvents()
    {
        PluginInfo = null;
        FixedLayout = null;
        Issue = null;
        RepoAddedEventSender.RemoveAll();
    }

    static bool Raise<T>( IActivityMonitor monitor, Action<T>? handler, T e ) where T : WorldEvent
    {
        if( handler != null )
        {
            try
            {
                handler( e );
                return e.Success;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While raising '{typeof( T ).Name}' event.", ex );
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Raised when <see cref="World.FixLayout"/> has been successfully called.
    /// </summary>
    public event Action<FixedAllLayoutEvent>? FixedLayout;

    internal bool SafeRaiseEvent( IActivityMonitor monitor, FixedAllLayoutEvent e ) => Raise( monitor, FixedLayout, e );

    /// <summary>
    /// Raised when plugin information is required.
    /// </summary>
    public event Action<PluginInfoEvent>? PluginInfo;

    internal bool SafeRaiseEvent( IActivityMonitor monitor, PluginInfoEvent e ) => Raise( monitor, PluginInfo, e );

    /// <summary>
    /// Raised by "ckli issue".
    /// </summary>
    public event Action<IssueEvent>? Issue;

    internal bool SafeRaiseEvent( IActivityMonitor monitor, IssueEvent e ) => Raise( monitor, Issue, e );

    /// <summary>
    /// Raised by "ckli repo add" and "ckli repo create" commands.
    /// </summary>
    public PerfectEvent<RepoAddedEvent> RepoAdded => RepoAddedEventSender.PerfectEvent;

}
