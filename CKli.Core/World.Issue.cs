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
        readonly bool _manualFix;

        Issue( string title, IRenderable body, Repo? repo, bool manualFix )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( title );
            Throw.CheckNotNullArgument( body );
            _title = title;
            _body = body;
            _repo = repo;
            _manualFix = manualFix;
        }

        /// <summary>
        /// Initializes a new Issue.
        /// </summary>
        /// <param name="title">The title.</param>
        /// <param name="body">The body. Can be <see cref="ScreenType.Unit"/>.</param>
        /// <param name="repo">The repository if this issue is related to a specific repository.</param>
        protected Issue( string title, IRenderable body, Repo? repo )
            : this( title, body, repo, false )
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
        public bool ManualFix => _manualFix;

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
                : base( title, body, repo, true )
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

        internal IRenderable ToRenderable( ScreenType screenType )
        {
            var title = _title;
            if( _manualFix ) title = "✋ " + title; 
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
            int autoFixCount = issues.Count( i => !i.ManualFix );
            int manualFixCount = issues.Count - autoFixCount;
            if( manualFixCount > 0 )
            {
                monitor.Warn( $"Found {issues.Count} issues that require a manual fix." );
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
                if( autoFixCount > 0 )
                {
                    using( monitor.OpenInfo( manualFixCount > 0
                                                ? $"Trying to fix {autoFixCount} issues ({manualFixCount} issues must be fixed manually)."
                                                : $"Trying to fix {autoFixCount} issues." ) )
                    {
                        foreach( var g in groupedIssues.Where( g => g.Any() ) )
                        {
                            using( monitor.OpenInfo( $"Handling issues for '{g.Key?.DisplayPath ?? _name.FullName}'." ) )
                            {
                                foreach( var i in g )
                                {
                                    try
                                    {
                                        if( !i.ManualFix )
                                        {
                                            if( !await i.ExecuteAsync( monitor, context, this, cancellation ).ConfigureAwait( false ) )
                                            {
                                                monitor.CloseGroup( $"Fixing '{i.Title}' failed." );
                                                return false;
                                            }
                                        }
                                        monitor.Info( $"Fixed '{i.Title}'." );
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
