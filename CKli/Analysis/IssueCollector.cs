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

        internal void Add( IReadOnlyList<ActivityMonitorSimpleCollector.Entry> entries, string title, Func<IActivityMonitor, bool> fix )
        {
            var maxLevel = LogLevel.None;
            StringBuilder b = new StringBuilder();
            foreach( var e in entries )
            {
                string level = e.MaskedLevel.ToString();
                b.Append( level ).Append( " - " ).AppendLine( e.Text );
                if( e.Exception != null ) b.AppendMultiLine( new string( ' ', level.Length + 3 ), e.Exception.Message, true );
                if( maxLevel < e.MaskedLevel ) maxLevel = e.MaskedLevel;
            }
            var desc = b.ToString();
            _issues.Add( new Issue( _issues.Count, maxLevel, title, desc, fix ) );
        }
    }
}
