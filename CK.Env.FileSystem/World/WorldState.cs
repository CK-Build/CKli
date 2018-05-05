using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using CK.Core;

namespace CK.Env
{
    public class WorldState
    {
        static readonly XName xGlobalGitStatus = XNamespace.None + "GlobalGitStatus";
        static readonly XName xXmlState = XNamespace.None + "XmlState";

        readonly IWorldName _world;
        readonly XDocument _doc;
        readonly XElement _xmlState;

        public WorldState( IWorldName w, XDocument d )
        {
            if( w == null ) throw new ArgumentNullException( nameof( w ) );
            if( d == null ) throw new ArgumentNullException( nameof( d ) );
            if( d.Root.Name != w.FullName+"-World-State" ) throw new ArgumentException( $"Invalid state document root. Must be {w.FullName+"-World-State"}, found {d.Root.Name}.", nameof( d ) );
            _world = w;
            _doc = d;
            GlobalGitStatus = d.Root.AttributeEnum( xGlobalGitStatus, CK.Env.GlobalGitStatus.Unknwon );
            _xmlState = d.Root.Element( xXmlState );
            if( _xmlState == null )
            {
                _xmlState = new XElement( xXmlState );
                _doc.Root.Add( _xmlState );
            }
        }

        public WorldState( IWorldName w )
        {
            if( w == null ) throw new ArgumentNullException( nameof( w ) );
            _world = w;
            _doc = new XDocument( new XElement( w.FullName+"-State", new XAttribute( xGlobalGitStatus, GlobalGitStatus.ToString() ) ) );
            _xmlState = new XElement( xXmlState );
            _doc.Root.Add( _xmlState );
        }

        public IWorldName World => _world;

        public GlobalGitStatus GlobalGitStatus { get; set; }

        public XElement XmlState => _xmlState;

        public XDocument GetXDocument()
        {
            _doc.Root.SetAttributeValue( xGlobalGitStatus, GlobalGitStatus.ToString() );
            return _doc;
        }

        public override string ToString() => _doc.ToString();

    }
}
