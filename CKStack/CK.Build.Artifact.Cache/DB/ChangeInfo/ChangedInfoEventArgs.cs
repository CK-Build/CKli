using CK.Core;

namespace CK.Build.PackageDB
{
    /// <summary>
    /// Event argument for <see cref="PackageCache.DBChanged"/>.
    /// </summary>
    public sealed class ChangedInfoEventArgs : EventMonitoredArgs
    {
        public ChangedInfoEventArgs( IActivityMonitor monitor, ChangedInfo info )
            : base( monitor )
        {
            Throw.CheckNotNullArgument( info );
            Info = info;
        }

        public ChangedInfo Info { get; }
    }

}

