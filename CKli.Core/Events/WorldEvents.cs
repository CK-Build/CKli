using CK.Core;
using CK.PerfectEvent;
using System;

namespace CKli.Core;

/// <summary>
/// Exposes the <see cref="World.Events"/> to which <see cref="PluginBase"/> can subscribe.
/// </summary>
public sealed class WorldEvents
{
    internal readonly PerfectEventSender<RepoAddedEventArgs> _repoAddedEventSender;
    internal readonly PerfectEventSender<CreateLTSEventArgs> _createLTSEventSender;
    internal readonly PerfectEventSender<StackMergedEventArgs> _stackMergedEventSender;

    internal WorldEvents()
    {
        _repoAddedEventSender = new PerfectEventSender<RepoAddedEventArgs>();
        _createLTSEventSender = new PerfectEventSender<CreateLTSEventArgs>();
        _stackMergedEventSender = new PerfectEventSender<StackMergedEventArgs>();
    }

    internal void ReleaseEvents()
    {
        PluginInfo = null;
        FixedLayout = null;
        Issue = null;
        _repoAddedEventSender.RemoveAll();
        _createLTSEventSender.RemoveAll();
        _stackMergedEventSender.RemoveAll();
    }

    static bool Raise<T>( IActivityMonitor monitor, Action<T>? handler, T e ) where T : WorldEventArgs
    {
        if( handler != null )
        {
            try
            {
                handler( e );
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
    public event Action<FixedAllLayoutEventArgs>? FixedLayout;

    internal bool SafeRaiseEvent( IActivityMonitor monitor, FixedAllLayoutEventArgs e ) => Raise( monitor, FixedLayout, e );

    /// <summary>
    /// Raised when plugin information is required.
    /// </summary>
    public event Action<PluginInfoEventArgs>? PluginInfo;

    internal bool SafeRaiseEvent( IActivityMonitor monitor, PluginInfoEventArgs e ) => Raise( monitor, PluginInfo, e );

    /// <summary>
    /// Raised by "ckli issue".
    /// </summary>
    public event Action<IssueEventArgs>? Issue;

    internal bool SafeRaiseEvent( IActivityMonitor monitor, IssueEventArgs e ) => Raise( monitor, Issue, e );

    /// <summary>
    /// Raised by "ckli repo add" and "ckli repo create" commands.
    /// </summary>
    public PerfectEvent<RepoAddedEventArgs> RepoAdded => _repoAddedEventSender.PerfectEvent;

    /// <summary>
    /// Raised by "ckli world lts create" command.
    /// </summary>
    public PerfectEvent<CreateLTSEventArgs> CreateLTS => _createLTSEventSender.PerfectEvent;

    /// <summary>
    /// Raised by <see cref="World.PullStackAsync(IActivityMonitor)"/> when the pull of the Stack created a merge commit:
    /// a plugin that owns a file derived from others (declared as <c>merge=union</c> in the Stack's ".gitattributes")
    /// rebuilds it.
    /// </summary>
    public PerfectEvent<StackMergedEventArgs> StackMerged => _stackMergedEventSender.PerfectEvent;

}
