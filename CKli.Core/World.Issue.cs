using CK.Core;
using System.Threading.Tasks;

namespace CKli.Core;

public sealed partial class World
{
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
        /// Gets whether this issue cannot be fixed automatically.
        /// Use <see cref="CreateManual(string, IRenderable, Repo?)"/> to create such issues.
        /// </summary>
        public bool ManualFix => _manualFix;

        /// <summary>
        /// Executes the fix.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="context">The CKli environment.</param>
        /// <param name="world">The World.</param>
        /// <returns>True on success, false on error.</returns>
        internal protected abstract ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world );

        sealed class Manual : Issue
        {
            public Manual( string title, IRenderable body, Repo? repo )
                : base( title, body, repo, true )
            {
            }

            protected internal override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
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
            return new Collapsable( screenType.Text( _title ).AddBelow( _body ) );
        }
    }

}
