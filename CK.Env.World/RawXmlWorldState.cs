using CK.Core;
using System;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Basic encapsulation of a <see cref="XDocument"/>.
    /// </summary>
    public class RawXmlWorldState
    {
        static readonly XName xWorkStatusName = XNamespace.None + "WorkStatus";
        static readonly XName xOtherOperationName = XNamespace.None + "OperationName";
        static readonly XName xGeneralStateName = XNamespace.None + "GeneralState";
        static readonly XName xWorkStatesName = XNamespace.None + "WorkStates";

        readonly XDocument _doc;
        readonly IWorldName _world;
        readonly XElement _generalState;
        readonly XElement _workStates;

        public RawXmlWorldState( IWorldName w, XDocument d )
        {
            if( w == null ) throw new ArgumentNullException( nameof( w ) );
            if( d == null ) throw new ArgumentNullException( nameof( d ) );
            if( d.Root.Name != w.FullName+"-World-State" ) throw new ArgumentException( $"Invalid state document root. Must be {w.FullName+"-World-State"}, found {d.Root.Name}.", nameof( d ) );
            _world = w;
            _doc = d;
            _generalState = d.Root.EnsureElement( xGeneralStateName );
            _workStates = d.Root.EnsureElement( xWorkStatesName );
        }

        public RawXmlWorldState( IWorldName w )
            : this( w, new XDocument( new XElement( w.FullName + "-World-State", new XAttribute( xWorkStatusName, GlobalWorkStatus.Idle.ToString() ) ) ) )
        {
        }

        /// <summary>
        /// Gets the world.
        /// </summary>
        public IWorldName World => _world;

        /// <summary>
        /// Gets or sets the current global status.
        /// </summary>
        public GlobalWorkStatus WorkStatus
        {
            get => _doc.Root.AttributeEnum( xWorkStatusName, CK.Env.GlobalWorkStatus.Idle );
            set => _doc.Root.SetAttributeValue( xWorkStatusName, value.ToString() );
        }

        /// <summary>
        /// Gets or sets the operation name (when <see cref="WorkStatus"/> is <see cref="GlobalWorkStatus.OtherOperation"/>).
        /// </summary>
        public string OtherOperationName
        {
            get => (string)_doc.Root.Attribute( xOtherOperationName );
            set => _doc.Root.SetAttributeValue( xOtherOperationName, value );
        }

        /// <summary>
        /// Gets the <see cref="XElement"/> general state.
        /// This is where state information that are not specific to an operation should be stored.
        /// </summary>
        public XElement GeneralState => _generalState;

        /// <summary>
        /// Gets the <see cref="XElement"/> state for an operation.
        /// </summary>
        /// <param name="status">The work status.</param>
        /// <returns>The element to use.</returns>
        public XElement GetWorkState( GlobalWorkStatus status )
        {
            return _workStates.EnsureElement( status.ToString() );
        }

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
