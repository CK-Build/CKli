using CK.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace CKli.Core;

public sealed partial class World
{
    /// <summary>
    /// Models a World issue.
    /// </summary>
    public abstract class Issue
    {
        readonly string _title;
        readonly IRenderable _body;
        readonly Repo? _repo;
        readonly Kind _kind;

        /// <summary>
        /// Qualifies the type of the <see cref="Issue"/>.
        /// </summary>
        public enum Kind
        {
            /// <summary>
            /// Non applicable.
            /// </summary>
            None,

            /// <summary>
            /// Implicit issue is automatically and implicitly fixed.
            /// They appear in "ckli issue" only because no CKli execution run before and fixed them silently.
            /// <para>
            /// They appear in dark gray in the display.
            /// </para>
            /// </summary>
            Implicit,

            /// <summary>
            /// The issue can be automatically fixed by using "ckli issue --fix".
            /// </summary>
            AutomaticFix,

            /// <summary>
            /// The issue must be fixed manually. "ckli issue --fix" cannot fix it.
            /// <para>
            /// A ✋ appears in the display.
            /// </para>
            /// </summary>
            ManualFix
        }

        Issue( string title, IRenderable body, Repo? repo, Kind kind )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( title );
            Throw.CheckNotNullArgument( body );
            Throw.CheckArgument( kind != Kind.None );
            _title = title;
            _body = body;
            _repo = repo;
            _kind = kind;
        }

        /// <summary>
        /// Initializes a new <see cref="Kind.Implicit"/> or <see cref="Kind.AutomaticFix"/> Issue.
        /// Use <see cref="CreateManual(string, IRenderable, Repo?)"/> for manual issues.
        /// </summary>
        /// <param name="title">The title.</param>
        /// <param name="body">The body. Can be <see cref="ScreenType.Unit"/>.</param>
        /// <param name="repo">The repository if this issue is related to a specific repository.</param>
        /// <param name="implicitIssue">True for a <see cref="Kind.Implicit"/> issue. Defaults to <see cref="Kind.AutomaticFix"/>.</param>
        protected Issue( string title, IRenderable body, Repo? repo, bool implicitIssue = false )
            : this( title, body, repo, implicitIssue ? Kind.Implicit : Kind.AutomaticFix )
        {
        }

        /// <summary>
        /// Gets the issue title.
        /// </summary>
        public string Title => _title;

        /// <summary>
        /// Gets the issue body. May be <see cref="ScreenType.Unit"/>.
        /// </summary>
        public IRenderable Body => _body;

        /// <summary>
        /// Gets the repository to which this issue applies if any.
        /// </summary>
        public Repo? Repo => _repo;

        /// <summary>
        /// Gets whether this issue cannot be fixed automatically (a ✋ appears).
        /// Use <see cref="CreateManual(string, IRenderable, Repo?)"/> to create such issues.
        /// </summary>
        public Kind IssueKind => _kind;

        /// <summary>
        /// Executes the fix.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="context">The CKli environment.</param>
        /// <param name="world">The World.</param>
        /// <param name="scopeAlive">Cancellation token from the current <see cref="InterruptibleScope"/>.</param>
        /// <returns>True on success, false on error.</returns>
        internal protected abstract ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world, CancellationToken scopeAlive );

        sealed class Manual : Issue
        {
            public Manual( string title, IRenderable body, Repo? repo )
                : base( title, body, repo, Kind.ManualFix )
            {
            }

            protected internal override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world, CancellationToken cancellation )
            {
                return ValueTask.FromResult( false );
            }
        }

        /// <summary>
        /// Creates an issue that requires a manual fix.
        /// </summary>
        /// <param name="title">The title. Must not be empty or whitespace.</param>
        /// <param name="body">The body.</param>
        /// <param name="repo">The Repo if this issue is related to a Repo.</param>
        /// <returns>An issue that must be manually fixed.</returns>
        public static Issue CreateManual( string title, IRenderable body, Repo? repo ) => new Manual( title, body, repo );

        /// <summary>
        /// Gets the display of this issue: the <see cref="Title"/> (prefixed by a marker that depends on
        /// the <see cref="IssueKind"/>) above the <see cref="Body"/>, in a <see cref="Collapsable"/> that
        /// is entirely dark gray for a <see cref="Kind.Implicit"/> issue.
        /// </summary>
        /// <param name="screenType">The screen type to use.</param>
        /// <returns>The renderable display of this issue.</returns>
        public IRenderable ToRenderable( ScreenType screenType )
        {
            var title = _title;
            if( _kind is Kind.Implicit )
            {
                title = "Ⓘ " + title;
                return new Collapsable( screenType.Text( title ).AddBelow( _body ), new TextStyle( ConsoleColor.DarkGray ) );
            }
            if( _kind is Kind.ManualFix ) title = "✋ " + title;
            else title = "⚙ " + title;
            return new Collapsable( screenType.Text( title ).AddBelow( _body ) );
        }
    }

    internal async Task<bool> HandleIssuesAsync( IActivityMonitor monitor,
                                                 CKliEnv context,
                                                 List<Issue> issues,
                                                 bool displayIssues,
                                                 bool applyAutoFixes,
                                                 CancellationToken cancellation )
    {
        if( issues.Count == 0 )
        {
            monitor.Info( ScreenType.CKliScreenTag, "No issues found." );
        }
        else
        {
            int manualFixCount = issues.Count( i => i.IssueKind == Issue.Kind.ManualFix );
            // Implicit issues are fixed like the automatic ones: only their display differs.
            int fixableCount = issues.Count - manualFixCount;
            if( manualFixCount > 0 )
            {
                monitor.Warn( $"Found {manualFixCount} issues that require a manual fix." );
            }
            if( displayIssues )
            {
                foreach( var g in issues.GroupBy( i => i.Repo ).OrderBy( g => g.Key?.Index ?? -1 ) )
                {
                    var link = g.Key == null
                                ? context.Screen.ScreenType.Text( _name.FullName )
                                    .HyperLink( new Uri( _name.WorldRoot ) )
                                : context.Screen.ScreenType.Text( g.Key.DisplayPath )
                                    .HyperLink( new Uri( g.Key.WorkingFolder ) );
                    var header = link.Box( marginRight: 1 ).AddRight( context.Screen.ScreenType.Text( $"({g.Count()})", TextEffect.Italic ) );
                    var repo = header.AddBelow( g.Select( i => i.ToRenderable( context.Screen.ScreenType ) ) );
                    context.Screen.Display( new Collapsable( repo ) );
                }
            }
            if( applyAutoFixes )
            {
                // Applies always the same ordering.
                var groupedIssues = issues.GroupBy( i => i.Repo ).OrderBy( g => g.Key?.Index ?? -1 );
                if( fixableCount > 0 )
                {
                    using( monitor.OpenInfo( manualFixCount > 0
                                                ? $"Trying to fix {fixableCount} issues ({manualFixCount} issues must be fixed manually)."
                                                : $"Trying to fix {fixableCount} issues." ) )
                    {
                        foreach( var g in groupedIssues.Where( g => g.Any() ) )
                        {
                            using( monitor.OpenInfo( $"Handling issues for '{g.Key?.DisplayPath ?? _name.FullName}'." ) )
                            {
                                foreach( var i in g )
                                {
                                    try
                                    {
                                        // Executes Automatic and Implicit.
                                        if( i.IssueKind is not Issue.Kind.ManualFix )
                                        {
                                            if( !await i.ExecuteAsync( monitor, context, this, cancellation ).ConfigureAwait( false ) )
                                            {
                                                monitor.CloseGroup( $"Fixing '{i.Title}' failed." );
                                                return false;
                                            }
                                            monitor.Info( $"Fixed '{i.Title}'." );
                                        }
                                    }
                                    catch( OperationCanceledException ex ) when( ex.CancellationToken == cancellation )
                                    {
                                        return false;
                                    }
                                    catch( Exception ex )
                                    {
                                        monitor.Error( $"While fixing '{i.Title}'.", ex );
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        return true;
    }

}
