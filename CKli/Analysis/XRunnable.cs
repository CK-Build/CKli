using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CK.Env.Analysis
{
    public class XRunnable : XTypedObject
    {
        public interface IRunContext
        {
            /// <summary>
            /// Gets the monitor to use.
            /// </summary>
            IActivityMonitor Monitor { get; }

            /// <summary>
            /// Gets a shared global dictionary for the run.
            /// </summary>
            Dictionary<object, object> Items { get; }
        }

        /// <summary>
        /// Default implementation of the <see cref="IRunContext"/>.
        /// </summary>
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
            Initializer initializer,
            XRunnable parent = null )
            : base( initializer )
        {
            SkipRun |= parent?.SkipRun ?? false;
        }

        public bool SkipRun { get; private set; }

        /// <summary>
        /// Runs this object only if <see cref="SkipRun"/> is false.
        /// </summary>
        /// <param name="ctx">The run context.</param>
        /// <returns>True on success, false if a severe error occurred.</returns>
        public bool Run( IRunContext ctx )
        {
            if( SkipRun ) ctx.Monitor.Trace( $"Skipping {ToString()}." );
            else
            {
                using( ctx.Monitor.OpenInfo( $"Running {ToString()}." ) )
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
        /// Calls <see cref="Run"/> on <see cref="XTypedObject.TopDescendants"/> that
        /// are <see cref="XRunnable"/> objects.
        /// </summary>
        /// <param name="ctx">The run context.</param>
        /// <returns>True on success, false if a severe error occurred.</returns>
        protected virtual bool RunChildren( IRunContext ctx )
        {
            foreach( var c in TopDescendants( d => d is XRunnable ).Cast<XRunnable>() )
            {
                if( !c.Run( ctx ) ) return false;
            }
            return true;
        }
    }
}
