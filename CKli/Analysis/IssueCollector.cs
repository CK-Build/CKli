using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CK.Env.Analysis
{
    public class IssueCollector
    {
        readonly List<Issue> _issues;

        class Issue : IIssue
        {
            readonly Func<IActivityMonitor,bool> _fix;

            public Issue(
                int n,
                LogLevel max,
                string identifier,
                string title,
                string description,
                Func<IActivityMonitor,bool> fix )
            {
                Number = n;
                MaxLevel = max;
                Title = title;
                Description = description;
                _fix = fix;
            }

            public int Number { get; }

            public LogLevel MaxLevel { get; }

            public string Identifier { get; }

            public string Title { get; }

            public string Description { get; }

            public bool HasAutoFix => _fix != null;

            public bool AutoFix( IActivityMonitor monitor )
            {
                return _fix?.Invoke( monitor ) ?? true;
            }

            public string ToString( bool withDescription )
            {
                var s = ToString();
                if( withDescription ) s += Environment.NewLine + Description;
                return s;
            }
            public override string ToString() => $"{Number} {(HasAutoFix ? "*" : " ")} - {MaxLevel} - {Title}";

        }

        public IssueCollector()
        {
            _issues = new List<Issue>();
        }

        /// <summary>
        /// Gets or sets whether collecting issue is disabled.
        /// </summary>
        public bool Disabled { get; set; }

        public void Clear() => _issues.Clear();

        public IReadOnlyList<IIssue> Issues => _issues;

        public void DisplayIssues( TextWriter w, bool withDescription )
        {
            int fixCount = 0;
            foreach( var i in _issues )
            {
                if( i.HasAutoFix ) ++fixCount;
                Console.WriteLine( i.ToString( withDescription ) );
            }
            if( fixCount > 0 )
            {
                Console.WriteLine( $"{fixCount} automatic fixes available." );
            }
        }

        public bool FixIssue( IActivityMonitor m, int issueNumber )
        {
            IIssue i = _issues[issueNumber];
            if( !i.HasAutoFix ) throw new ArgumentOutOfRangeException( nameof( issueNumber ) );
            using( m.OpenInfo( $"Fixing {i.ToString()}" ) )
            {
                return i.AutoFix( m );
            }
        }

        void Add( IReadOnlyList<ActivityMonitorSimpleCollector.Entry> entries,
                  string identifier,
                  string title,
                  Func<IActivityMonitor, bool> fix )
        {
            var maxLevel = LogLevel.None;
            StringBuilder b = new StringBuilder();
            const string exPrefix = "    ";
            foreach( var e in entries )
            {
                char level = e.MaskedLevel.ToString()[0];
                b.Append( level ).Append( " - " ).AppendLine( e.Text );
                if( e.Exception != null ) b.AppendMultiLine( exPrefix, e.Exception.Message, true ).AppendLine();
                if( maxLevel < e.MaskedLevel ) maxLevel = e.MaskedLevel;
            }
            b.Append( "Identifier: " ).AppendLine( identifier );
            var desc = b.ToString();
            _issues.Add( new Issue( _issues.Count, maxLevel, identifier, title, desc, fix ) );
        }

        /// <summary>
        /// Implementation of <see cref="IIssueBuilder"/>.
        /// </summary>
        public class IssueBuilder : IIssueBuilder
        {
            readonly ActivityMonitorSimpleCollector _logCollector;
            readonly IActivityMonitor _monitor;
            internal string _fixIdentifier;
            internal string _fixTitle;
            internal Func<IActivityMonitor, bool> _autoFixAction;

            internal protected IssueBuilder( IActivityMonitor monitor )
            {
                _logCollector = new ActivityMonitorSimpleCollector() { MinimalFilter = LogLevelFilter.Debug };
                _monitor = monitor;
            }

            public IActivityMonitor Monitor => _monitor;

            internal void StartLogCollect()
            {
                Monitor.Output.RegisterClient( _logCollector );
            }

            public void CreateIssue( string identifier, string title = null, Func<IActivityMonitor, bool> autoFix = null )
            {
                if( identifier == null ) throw new ArgumentNullException( nameof( identifier ) );
                if( _fixIdentifier != null ) throw new InvalidOperationException( $"Fix has already been created." );
                _fixIdentifier = identifier;
                _fixTitle = title;
                _autoFixAction = autoFix;
            }

            internal IReadOnlyList<ActivityMonitorSimpleCollector.Entry> StopLogCollect()
            {
                Monitor.Output.RegisterClient( _logCollector );
                return _logCollector.Entries;
            }
        }

        /// <summary>
        /// Actual run of an issue factory function.
        /// To be used only to support specialized builders.
        /// </summary>
        /// <typeparam name="T">Type of the builder. Must be a <see cref="IssueBuilder"/>.</typeparam>
        /// <typeparam name="TI">Type of the builder contract provided to the factory function.</typeparam>
        /// <param name="builder">The builder object.</param>
        /// <param name="factory">The factory for issue. Must return false if an error occured.</param>
        /// <returns>True on success, false if an error occured.</returns>
        public bool RunIssueBuilder<T,TI>( T builder, Func<TI, bool> factory )
            where T : IssueBuilder, TI
            where TI : IIssueBuilder
        {
            try
            {
                builder.StartLogCollect();
                bool success = factory( builder );
                var entries = builder.StopLogCollect();
                if( !success ) return false;
                if( builder._fixTitle != null )
                {
                    Add( entries, builder._fixIdentifier, builder._fixTitle, builder._autoFixAction );
                }
                return true;
            }
            catch( Exception ex )
            {
                builder.Monitor.Fatal( $"Internal error.", ex );
                return false;
            }
        }

        /// <summary>
        /// Runs a factory function that can emit one issue with an optional automatic fix.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="factory">The factory for issue. Must return false if an error occured.</param>
        /// <returns>True on success, false if an error occured.</returns>
        public bool RunIssueFactory( IActivityMonitor m, Func<IIssueBuilder, bool> factory )
        {
            return RunIssueBuilder( new IssueBuilder( m ), factory );
        }

    }
}
