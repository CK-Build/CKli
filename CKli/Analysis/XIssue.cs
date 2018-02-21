using CKli;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Analysis
{
    public abstract class XIssue : XRunnable
    {
        readonly IssueCollector _collector;

        public XIssue(
            IssueCollector collector,
            Initializer intializer )
            : base( intializer )
        {
            _collector = collector;
        }

        public interface IRunContextIssue : IRunContext
        {
            void CreateFix( string title, Func<IActivityMonitor, bool> autoFix = null );
        }

        class RunContextIssue : IRunContextIssue
        {
            readonly IRunContext _base;
            readonly XIssue _issue;
            readonly ActivityMonitorSimpleCollector _sc;
            internal string _fixTitle;
            internal Func<IActivityMonitor, bool> _autoFixAction;

            public RunContextIssue( IRunContext c, XIssue issue, ActivityMonitorSimpleCollector sc )
            {
                _base = c;
                _issue = issue;
                _sc = sc;
            }

            public IActivityMonitor Monitor => _base.Monitor;

            public Dictionary<object, object> Items => _base.Items;

            public void CreateFix( string title, Func<IActivityMonitor, bool> autoFix = null )
            {
                if( title == null ) throw new ArgumentNullException( nameof( title ) );
                if( _fixTitle != null ) throw new InvalidOperationException( $"Fix has already been created." );
                _fixTitle = title;
                _autoFixAction = autoFix;
            }

            internal ActivityMonitorSimpleCollector ActivityMonitorSimpleCollector => _sc;
        }

        protected sealed override bool DoRun( IRunContext ctx )
        {
            var logCollector = new ActivityMonitorSimpleCollector() { MinimalFilter = LogLevelFilter.Debug };
            ctx.Monitor.Output.RegisterClient( logCollector );
            try
            {
                var ctxIssue = new RunContextIssue( ctx, this, logCollector );
                bool success = DoRun( ctxIssue );
                if( !success ) return false;
                if( ctxIssue._fixTitle != null )
                {
                    _collector.Add( logCollector.Entries, ctxIssue._fixTitle, ctxIssue._autoFixAction );
                }
                return true;
            }
            catch( Exception ex )
            {
                ctx.Monitor.Fatal( $"Internal error.", ex );
                return false;
            }
            finally
            {
                ctx.Monitor.Output.UnregisterClient( logCollector );
            }
        }

        protected abstract bool DoRun( IRunContextIssue ctx );

        protected override bool RunChildren( IRunContext ctx )
        {
            var iC = (RunContextIssue)ctx;
            try
            {
                ctx.Monitor.Output.UnregisterClient( iC.ActivityMonitorSimpleCollector );
                return base.RunChildren( ctx );
            }
            finally
            {
                ctx.Monitor.Output.RegisterClient( iC.ActivityMonitorSimpleCollector );
            }
        }

    }
}
