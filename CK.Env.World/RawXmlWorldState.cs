using CK.Core;
using System;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates local and shared <see cref="XDocument"/> states.
    /// </summary>
    public class RawXmlWorldState
    {
        readonly IWorldName _world;
        readonly XDocument _doc;
        readonly XElement _generalState;
        readonly XElement _lastBuild;
        readonly XElement _builds;
        readonly XElement _releaseBuildResult;
        readonly XElement _ciBuildResult;
        readonly XElement _localBuildResult;
        readonly XElement _publishedBuildHistory;
        readonly BuildResult[] _buildResults;

        public RawXmlWorldState( IWorldName w, XDocument d )
        {
            if( w == null ) throw new ArgumentNullException( nameof( w ) );
            if( d == null ) throw new ArgumentNullException( nameof( d ) );
            string safeName = SafeElementName( w );
            _world = w;

            if( d.Root.Name != safeName ) throw new ArgumentException( $"Invalid state document root. Must be {safeName}, found {d.Root.Name}.", nameof( d ) );
            _doc = d;
            _generalState = d.Root.EnsureElement( XmlNames.xGeneralState );
            _lastBuild = d.Root.EnsureElement( XmlNames.xLastBuild );
            _builds = d.Root.EnsureElement( XmlNames.xBuilds );
            _releaseBuildResult = _builds.EnsureElement( XmlNames.xRelease );
            _ciBuildResult = _builds.EnsureElement( XmlNames.xCI );
            _localBuildResult = _builds.EnsureElement( XmlNames.xLocal );
            _publishedBuildHistory = d.Root.EnsureElement( XmlNames.xPublishedBuildHistory );
            LastBuildType = _lastBuild.AttributeEnum( XmlNames.xType, BuildResultType.None );
            _buildResults = new BuildResult[3];
        }

        public RawXmlWorldState( IWorldName w )
            : this( w, new XDocument( new XElement( SafeElementName( w ), new XAttribute( XmlNames.xWorkStatus, GlobalWorkStatus.Idle.ToString() ) ) ) )
        {
        }

        static string SafeElementName( IWorldName w ) => w.FullName.Replace( '[', '-' ).Replace( "]", "" ) + ".World.State";

        /// <summary>
        /// Gets the world.
        /// </summary>
        public IWorldName World => _world;

        /// <summary>
        /// Gets or sets the current global status.
        /// </summary>
        public GlobalWorkStatus WorkStatus
        {
            get => _doc.Root.AttributeEnum( XmlNames.xWorkStatus, GlobalWorkStatus.Idle );
            set => _doc.Root.SetAttributeValue( XmlNames.xWorkStatus, value.ToString() );
        }

        /// <summary>
        /// Gets the <see cref="XElement"/> general state.
        /// This is where state information should be stored.
        /// </summary>
        public XElement GeneralState => _generalState;

        /// <summary>
        /// Sets a build result, updating <see cref="LastBuildType"/>.
        /// </summary>
        /// <param name="r">The build result.</param>
        public void SetBuildResult( BuildResult r )
        {
            if( r == null ) throw new ArgumentNullException( nameof( r ) );
            if( r.Type == BuildResultType.None ) throw new ArgumentException( nameof( r ) );
            LastBuildType = r.Type;
            _lastBuild.SetAttributeValue( XmlNames.xType, r.Type.ToString() );
            var rXml = r.ToXml();
            switch( r.Type )
            {
                case BuildResultType.Local: _localBuildResult.ReplaceElementByName( rXml ); break;
                case BuildResultType.CI: _ciBuildResult.ReplaceElementByName( rXml ); break;
                case BuildResultType.Release: _releaseBuildResult.ReplaceElementByName( rXml ); break;
            }
            _buildResults[(int)r.Type - 1] = r; 
        }

        /// <summary>
        /// Gets the last build result for a build type or null if not found.
        /// </summary>
        /// <param name="type">The build type.</param>
        /// <returns>The build result or null.</returns>
        public BuildResult GetBuildResult( BuildResultType type )
        {
            if( type == BuildResultType.None ) return null;
            if( _buildResults[(int)type - 1]  == null )
            {
                XElement e = GetParentBuildResultElement( type );
                e = e?.Elements().FirstOrDefault();
                if( e != null ) _buildResults[(int)type - 1] = new BuildResult( e );
            }
            return _buildResults[(int)type - 1];
        }

        XElement GetParentBuildResultElement( BuildResultType type )
        {
            switch( type )
            {
                case BuildResultType.Local: return _localBuildResult;
                case BuildResultType.CI: return _ciBuildResult;
                case BuildResultType.Release: return _releaseBuildResult;
            }
            return null;
        }

        /// <summary>
        /// Publishes the build result for a build type: transfers the current xml result
        /// to the PublishedBuildHistory element.
        /// </summary>
        /// <param name="type">The build type.</param>
        public void PublishBuildResult( BuildResultType type )
        {
            if( type == BuildResultType.None ) throw new ArgumentException();
            var b = _buildResults[(int)type - 1];
            if( b == null ) throw new InvalidOperationException( $"No current BuildResultType '{type}'." );
            var e = GetParentBuildResultElement( type );
            var xmlBuild = e.Elements().First();
            _publishedBuildHistory.Add( xmlBuild );
            xmlBuild.Remove();
        }

        /// <summary>
        /// Gets the last build type.
        /// </summary>
        public BuildResultType LastBuildType { get; private set; }

        /// <summary>
        /// Gets the full document.
        /// </summary>
        /// <returns>The document.</returns>
        public XDocument Document => _doc;

        /// <summary>
        /// Overridden to return the xml document as a string.
        /// </summary>
        /// <returns>The xml.</returns>
        public override string ToString() => _doc.ToString();

    }
}
