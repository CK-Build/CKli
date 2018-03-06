using CKli;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Analysis
{
    /// <summary>
    /// Standard implementation for an issuer that can
    /// create one issue thanks to <see cref="CreateIssue"/>.
    /// </summary>
    public abstract class XIssuer : XRunnable
    {
        readonly IssueCollector _collector;

        public XIssuer(
            IssueCollector collector,
            Initializer intializer )
            : base( intializer )
        {
            _collector = collector;
        }

        public interface IRunContextIssue : IRunContext, IIssueBuilder
        {
            new IActivityMonitor Monitor { get; }

            bool SkipRunChildren { get; set; }
        }

        protected IssueCollector IssueCollector => _collector;

        class RunContextIssue : IssueCollector.IssueBuilder, IRunContextIssue
        {
            readonly IRunContext _base;

            public RunContextIssue( IRunContext c )
                : base( c.Monitor )
            {
                _base = c;
            }

            /// <summary>
            /// Gets the <see cref="XRunnable.IRunContext.Items"/> shared global dictionary.
            /// </summary>
            public Dictionary<object, object> Items => _base.Items;

            /// <summary>
            /// Gets or sets whether children issues should be ignored.
            /// Defaults to false.
            /// </summary>
            public bool SkipRunChildren { get; set; }

        }

        protected sealed override bool DoRun( IRunContext ctx )
        {
            if( _collector.Disabled ) return RunChildren( ctx );
            var c = new RunContextIssue( ctx );
            if( !_collector.RunIssueBuilder<RunContextIssue, IRunContextIssue>( c, CreateIssue ) ) return false;
            return c.SkipRunChildren ? true : RunChildren( ctx ); 
        }

        /// <summary>
        /// This method can create one issue thanks to the <see cref="IRunContextIssue"/>
        /// and more than one by using directly the <see cref="IssueCollector"/> if needed.
        /// </summary>
        /// <param name="builder">Builder for an issue.</param>
        /// <returns>True on success, false if an error occured.</returns>
        protected abstract bool CreateIssue( IRunContextIssue builder );

    }
}
