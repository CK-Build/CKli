using CK.Core;
using System;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Basic encapsulation of a <see cref="XDocument"/>.
    /// </summary>
    public class WorldState
    {
        static readonly XName xGlobalGitStatusName = XNamespace.None + "GlobalGitStatus";
        static readonly XName xWorkStatusName = XNamespace.None + "WorkStatus";
        static readonly XName xXmlStateName = XNamespace.None + "XmlState";

        readonly XDocument _doc;
        readonly IWorldName _world;
        readonly XElement _xmlState;

        public WorldState( IWorldName w, XDocument d )
        {
            if( w == null ) throw new ArgumentNullException( nameof( w ) );
            if( d == null ) throw new ArgumentNullException( nameof( d ) );
            if( d.Root.Name != w.FullName+"-World-State" ) throw new ArgumentException( $"Invalid state document root. Must be {w.FullName+"-World-State"}, found {d.Root.Name}.", nameof( d ) );
            _world = w;
            _doc = d;
            GlobalGitStatus = d.Root.AttributeEnum( xGlobalGitStatusName, StandardGitStatus.Unknwon );
            WorkStatus = d.Root.AttributeEnum( xWorkStatusName, CK.Env.GlobalWorkStatus.Idle );
            _xmlState = d.Root.Element( xXmlStateName );
            if( _xmlState == null )
            {
                _xmlState = new XElement( xXmlStateName );
                _doc.Root.Add( _xmlState );
            }
        }

        public WorldState( IWorldName w )
        {
            if( w == null ) throw new ArgumentNullException( nameof( w ) );
            _world = w;
            _doc = new XDocument( new XElement( w.FullName+"-State",
                                    new XAttribute( xWorkStatusName, WorkStatus.ToString() ),
                                    new XAttribute( xGlobalGitStatusName, GlobalGitStatus.ToString() ) ) );
            _xmlState = new XElement( xXmlStateName );
            _doc.Root.Add( _xmlState );
        }

        /// <summary>
        /// Gets the world.
        /// </summary>
        public IWorldName World => _world;

        /// <summary>
        /// Gets or sets the current git status that apllies to the whole world.
        /// </summary>
        public StandardGitStatus GlobalGitStatus { get; set; }

        /// <summary>
        /// Gets or sets the current global status.
        /// </summary>
        public GlobalWorkStatus WorkStatus { get; set; }

        /// <summary>
        /// Gets the state <see cref="XElement"/>.
        /// This is where state information should be stored.
        /// </summary>
        public XElement XmlState => _xmlState;

        /// <summary>
        /// Gets the full document.
        /// </summary>
        /// <returns>The document.</returns>
        public XDocument GetXDocument()
        {
            _doc.Root.SetAttributeValue( xWorkStatusName, WorkStatus.ToString() );
            _doc.Root.SetAttributeValue( xGlobalGitStatusName, GlobalGitStatus.ToString() );
            return _doc;
        }

        /// <summary>
        /// Overridden to return the xml document as a string.
        /// </summary>
        /// <returns>The xml.</returns>
        public override string ToString() => GetXDocument().ToString();

    }
}
