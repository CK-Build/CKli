using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Base class for <see cref="LocalWorldState"/> and <see cref="SharedWorldState"/>.
    /// </summary>
    public abstract class BaseWorldState
    {
        readonly IWorldStore _store;

        internal BaseWorldState( IWorldStore store, IWorldName w, bool isLocal, XDocument? d = null )
        {
            Throw.CheckNotNullArgument( store );
            Throw.CheckNotNullArgument( w );

            _store = store;
            World = w;

            string safeName = SafeElementName( w, isLocal );
            if( d == null ) d = new XDocument( new XElement( safeName, new XAttribute( XmlNames.xWorkStatus, GlobalWorkStatus.Idle.ToString() ) ) );
            else if( d.Root?.Name != safeName ) Throw.ArgumentException( nameof( d ), $"Invalid state document root. Must be '<{safeName}>', found '<{d.Root?.Name}>'." );
            d.Changed += ( o, e ) => IsStateDirty = true;

            XDocument = d;
        }

        static string SafeElementName( IWorldName w, bool isLocal ) => w.FullName.Replace( '[', '-' ).Replace( "]", "" ) + ".World." + (isLocal ? "" : "Shared") + "State";

        /// <summary>
        /// Gets the world name.
        /// </summary>
        public IWorldName World { get; }

        /// <summary>
        /// Internal Xml document.
        /// </summary>
        internal readonly XDocument XDocument;

        /// <summary>
        /// Gets whether this state is dirty.
        /// </summary>
        public bool IsStateDirty { get; private set; }

        /// <summary>
        /// Fires whenever an actual save occurred.
        /// </summary>
        public event EventHandler<EventMonitoredArgs> Saved;

        /// <summary>
        /// Saves this state. If <see cref="IsStateDirty"/> is false, nothing is done.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SaveState( IActivityMonitor m )
        {
            if( IsStateDirty )
            {
                if( !_store.SaveState( m, this ) ) return false;
                IsStateDirty = false;
                Saved?.Invoke( this, new EventMonitoredArgs( m ) );
            }
            return true;
        }

        /// <summary>
        /// Overridden to return the xml document as a string.
        /// </summary>
        /// <returns>The xml.</returns>
        public override string ToString() => XDocument.ToString();
    }

}
