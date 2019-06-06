using CK.Core;
using CK.Text;
using System.Collections.Generic;

namespace System.Xml.Linq
{
    /// <summary>
    /// Basic encapsulation of an <see cref="Element"/>, a monitor and a internal hash set
    /// that handles marked objects (attributes as well as elements) that offers helpers
    /// to read a simple Xml document.
    /// </summary>
    public readonly struct XElementReader
    {
        readonly HashSet<XObject> _handled;

        /// <summary>
        /// Initializes a new reader bound to an <see cref="Element"/>.
        /// </summary>
        /// <param name="m">The monitor that must be used.</param>
        /// <param name="element">The read element.</param>
        /// <param name="handled">The set of objects that has and will be handled by the read process.</param>
        public XElementReader(
            IActivityMonitor m,
            XElement element,
            HashSet<XObject> handled )
        {
            _handled = handled;
            Monitor = m;
            Element = element;
        }

        /// <summary>
        /// Get the monitor to use.
        /// </summary>
        public IActivityMonitor Monitor { get; }

        /// <summary>
        /// Emits a warning for each <see cref="Element"/>'s attribute that has not been <see cref="Handle"/>d.
        /// </summary>
        /// <returns>The number of emitted warnings.</returns>
        public int WarnUnhandledAttributes()
        {
            int warned = 0;
            foreach( var a in Element.Attributes() )
            {
                if( !_handled.Contains( a ) )
                {
                    warned++;
                    Monitor.Warn( $"Unhandled attribute '{a.Name}'{a.GetLineColumnString()}." );
                }
            }
            return warned;
        }

        /// <summary>
        /// Emits a warning if this <see cref="Element"/> has not been handled or for any of its own direct child element
        /// that has not been <see cref="Handle"/>d.
        /// </summary>
        /// <returns>The number of emitted warnings.</returns>
        public int WarnUnhandledElements()
        {
            int warned = 0;
            if( !_handled.Contains( Element ) )
            {
                warned = 1;
                Monitor.Warn( $"Unmapped element '{Element.Name}'{Element.GetLineColumnString()}." );
            }
            else foreach( var c in Element.Elements() )
                {
                    if( !_handled.Contains( c ) )
                    {
                        warned++;
                        Monitor.Warn( $"Unhandled element '{c.Name}'{c.GetLineColumnString()}." );
                    }
                }
            return warned;
        }

        /// <summary>
        /// Creates a reader bound to an element (typically a child).
        /// </summary>
        /// <param name="e">The element to be read.</param>
        /// <param name="handleElement">
        /// By default <see cref="Handle(XObject)"/> is called with <paramref name="e"/>.
        /// False to obtain a reader withiut handling the element.
        /// </param>
        /// <returns>A reader.</returns>
        public XElementReader WithElement( XElement e, bool handleElement = true )
        {
            var r = e == Element ? this : new XElementReader( Monitor, e, _handled );
            if( handleElement ) _handled.Add( e );
            return r;
        }

        /// <summary>
        /// Gets the raw <see cref="XElement"/>.
        /// </summary>
        public XElement Element { get; }

        /// <summary>
        /// Gets whether an object has been handled.
        /// </summary>
        /// <param name="o">The object.</param>
        /// <returns>Whether <see cref="Handle(XObject)"/> has been called on it.</returns>
        public bool HasBeenHandled( XObject o ) => _handled.Contains( o );

        /// <summary>
        /// Registers an object as beeing handled.
        /// </summary>
        /// <param name="o">The object to register.</param>
        /// <returns>True if it is the first time thios object is handled, false if it has already been handled.</returns>
        public bool Handle( XObject o ) => _handled.Add( o );

        /// <summary>
        /// Registers a set of object as beeing handled.
        /// </summary>
        /// <param name="o">The objects to register.</param>
        public void Handle( IEnumerable<XObject> o )
        {
            foreach( var i in o ) _handled.Add( i );
        }

        /// <summary>
        /// Simple helper to get a required <see cref="Element"/>'s attribute and call <see cref="Handle(XObject)"/>.
        /// Note that <see cref="Uri"/> and <see cref="NormalizedPath"/> types are handled.
        /// </summary>
        /// <param name="name">The attribute name that must exist.</param>
        /// <returns>The attribute.</returns>
        public T HandleRequiredAttribute<T>( XName name )
        {
            var a = Element.Attribute( name );
            if( a == null ) throw new System.Xml.XmlException( $"Required attribute '{name}'{Element.GetLineColumnString()}." );
            return HandleAttribute<T>( a );
        }

        /// <summary>
        /// Simple helper to get an optional <see cref="Element"/>'s attribute and call <see cref="Handle(XObject)"/>.
        /// Note that <see cref="Uri"/> and <see cref="NormalizedPath"/> types are handled.
        /// </summary>
        /// <param name="name">The attribute name.</param>
        /// <param name="defaultValue">Default value to use.</param>
        /// <returns>The attribute.</returns>
        public T HandleOptionalAttribute<T>( XName name, T defaultValue )
        {
            var a = Element.Attribute( name );
            if( a == null ) return defaultValue;
            return HandleAttribute<T>( a );
        }

        T HandleAttribute<T>( XAttribute a )
        {
            _handled.Add( a );
            return (T)Convert( a.Value, typeof( T ) );
        }

        /// <summary>
        /// Very simple model binder that sets attribute values to an object settable properties.
        /// </summary>
        /// <param name="target">The object instance target.</param>
        /// <param name="skipAlreadyHandledAttribute">False to apply already handled attributes.</param>
        public void SetPropertiesFromAttributes( object target, bool skipAlreadyHandledAttribute = true )
        {
            var a = Element.FirstAttribute;
            while( a != null )
            {
                var pInh = target.GetType().GetProperty( a.Name.LocalName );
                var p = pInh?.DeclaringType.GetProperty( pInh.Name );
                if( p != null
                    && p.CanWrite
                    && (Handle( a ) || !skipAlreadyHandledAttribute) )
                {
                    p.SetValue( target, Convert( a.Value, p.PropertyType ) );
                }
                a = a.NextAttribute;
            }
        }

        static object Convert( string value, Type t )
        {
            object result;
            if( t == typeof( NormalizedPath ) )
            {
                result = new NormalizedPath( value );
            }
            else if( t == typeof( Uri ) )
            {
                result = new Uri( value );
            }
            else if( t.IsEnum )
            {
                result = Enum.Parse( t, value );
            }
            else
            {
                result = System.Convert.ChangeType( value, t );
            }
            return result;
        }

        /// <summary>
        /// Handles add/remove/clear set of elements. This handles all the <see cref="Element"/>'s child
        /// named <paramref name="elementName"/>, each of them must contains only &lt;clear /&gt; or &lt;add .../ &lt;remove ...
        /// elements that the <paramref name="builder"/> function can project into <typeparamref name="T"/> instances.
        /// The <see cref="HashSet{T}.Comparer"/> must be correctly configured since it is the only responsible of the duplicate
        /// handling.
        /// </summary>
        /// <typeparam name="T">Set item type.</typeparam>
        /// <param name="elementName">
        /// The name of the collection element. Usually in the plural form (ie. "Things").
        /// </param>
        /// <param name="set">Current object set with a <see cref="HashSet{T}.Comparer"/> correctly configured.</param>
        /// <param name="builder">
        /// The builder that knons how to instanciate a <typeparamref name="T"/>
        /// from a &lt;add ... or a &lt;remove ... element.
        /// </param>
        /// <returns>The updated item <paramref name="set"/>.</returns>
        public HashSet<T> HandleCollection<T>( XName elementName, HashSet<T> set, Func<XElementReader, T> builder )
        {
            foreach( var c in Element.Elements( elementName ) )
            {
                _handled.Add( c );
                foreach( var e in c.Elements() )
                {
                    switch( e.Name.LocalName )
                    {
                        case "clear": set.Clear(); _handled.Add( e ); break;
                        case "remove": set.Remove( builder( WithElement( e ) ) ); break;
                        case "add":
                            {
                                var item = builder( WithElement( e ) );
                                if( !set.Add( item ) ) Throw( e, $"Item '{item}' already exists. There must be a <remove> first." );
                                break;
                            }
                        default: Throw( e, $"Expected only <add>, <remove> or <clear/> element in '{elementName}'." ); break;
                    }
                }
            }
            return set;
        }

        void Throw( XObject culprit, string message )
        {
            var p = culprit.GetLineColumnInfo();
            if( p.HasLineInfo() ) throw new XmlException( message + culprit.GetLineColumnString(), null, p.LineNumber, p.LinePosition );
            throw new XmlException( message );
        }

    }
}
