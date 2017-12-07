using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CKli
{
    public class XRunnable : XTypedObject
    {
        public interface IRunContext
        {
            IActivityMonitor Monitor { get; }

            Dictionary<object, object> Items { get; }
        }

        public class DefaultContext : IRunContext
        {
            readonly IActivityMonitor _monitor;
            readonly Dictionary<object, object> _items;

            public DefaultContext( IActivityMonitor m )
            {
                _monitor = m;
                _items = new Dictionary<object, object>();
            }

            public IActivityMonitor Monitor => _monitor;

            public Dictionary<object, object> Items => _items;
        }

        public XRunnable(
            Initializer initializer )
            : base( initializer )
        {
            var p = (XRunnable)initializer.Parent;
            Ignore |= p?.Ignore ?? false;
            XElementName = initializer.Element.Name.ToString();
        }

        public bool Ignore { get; private set; }

        string XElementName { get; }

        public new IEnumerable<XRunnable> Children => base.Children.Cast<XRunnable>();

        /// <summary>
        /// Runs this object only if <see cref="Ignore"/> is false.
        /// </summary>
        /// <param name="ctx">The run context.</param>
        /// <returns>True on success, false if a severe error occurred.</returns>
        public bool Run( IRunContext ctx )
        {
            if( Ignore ) ctx.Monitor.Trace( $"Skipping {XElementName}." );
            else
            {
                using( ctx.Monitor.OpenInfo( $"Running {XElementName}." ) )
                {
                    Reset( ctx );
                    return DoRun( ctx );
                }
            }
            return true;
        }

        /// <summary>
        /// Called before <see cref="Run"/>. Does nothing at this level.
        /// Should clear any cached information that may have been acquired by a previous run.
        /// </summary>
        /// <param name="ctx">The run context.</param>
        protected virtual void Reset( IRunContext ctx ) { }

        /// <summary>
        /// Only calls <see cref="RunChildren(IRunContext)"/> at this level.
        /// </summary>
        /// <param name="ctx">The run context.</param>
        /// <returns>True on success, false if a severe error occurred.</returns>
        protected virtual bool DoRun( IRunContext ctx ) => RunChildren( ctx );

        /// <summary>
        /// Calls <see cref="Run"/> on <see cref="Children"/>.
        /// </summary>
        /// <param name="ctx">The run context.</param>
        /// <returns>True on success, false if a severe error occurred.</returns>
        protected virtual bool RunChildren( IRunContext ctx )
        {
            foreach( var c in Children )
            {
                if( !c.Run( ctx ) ) return false;
            }
            return true;
        }
    }
}
