using CK.Core;
using CK.Text;
using System.Collections.Generic;
using System.Linq;

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
        /// <param name="monitor">The monitor that must be used. Can not be null.</param>
        /// <param name="element">The read element. Can not be null.</param>
        /// <param name="handled">The set of objects that has and will be handled by the read process. Can not be null.</param>
        public XElementReader(
            IActivityMonitor monitor,
            XElement element,
            HashSet<XObject> handled )
        {
            _handled = handled ?? throw new ArgumentNullException( nameof( handled ) );
            Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            Element = element ?? throw new ArgumentNullException( nameof( element ) );
        }

        /// <summary>
        /// Get whether this reader is valid: it is bound to a <see cref="Element"/> and
        /// has a valid <see cref="Monitor"/>.
        /// </summary>
        public bool IsValid => Element != null;

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
        /// Calls <see cref="WarnUnhandledElements"/> with warnings for attributes.
        /// </summary>
        /// <returns>The number of emitted warnings.</returns>
        public int WarnUnhandled() => WarnUnhandledElements( true );

        /// <summary>
        /// Emits a warning if this <see cref="Element"/> has not been handled or for any of its own direct child element
        /// that has not been <see cref="Handle"/>d.
        /// </summary>
        /// <returns>The number of emitted warnings.</returns>
        public int WarnUnhandledElements( bool withAttributes = false )
        {
            int warned = 0;
            if( !_handled.Contains( Element ) )
            {
                warned = 1;
                Monitor.Warn( $"Unmapped element '{Element.Name}'{Element.GetLineColumnString()}." );
            }
            else
            {
                if( withAttributes ) warned += WarnUnhandledAttributes();
                foreach( var c in Element.Elements() )
                {
                    if( !_handled.Contains( c ) )
                    {
                        warned++;
                        Monitor.Warn( $"Unhandled element '{c.Name}'{c.GetLineColumnString()}." );
                    }
                }
            }
            return warned;
        }

        /// <summary>
        /// Creates a reader bound to an element (typically a child).
        /// </summary>
        /// <param name="e">The element to be read.</param>
        /// <param name="handleElement">
        /// By default, you get a reader without handling the element.
        /// If <see langword="true"/> <see cref="Handle(XObject)"/> is called with <paramref name="e"/>.
        /// </param>
        /// <remarks>
        /// The handleElement is false by default: the purpose is to not handle by accident and silence the warning.
        /// This will generate more false positive, but will avoid handling things you didn't meant to.
        /// </remarks>
        /// <returns>A reader.</returns>
        public XElementReader WithElement( XElement e, bool handleElement = false )
        {
            var r = e == Element ? this : new XElementReader( Monitor, e, _handled );
            if( handleElement ) _handled.Add( e );
            return r;
        }

        /// <summary>
        /// Creates a reader bound to a required name child (and declares the child as
        /// being <see cref="Handle(XObject)">handled</see>.
        /// </summary>
        /// <param name="name">The name of child element.</param>
        /// <returns>A vaild reader.</returns>
        public XElementReader WithRequiredChild( XName name )
        {
            var c = Element.Element( name );
            if( c == null ) ThrowXmlException( $"Required '{name}' element." );
            return WithElement( c, true );
        }

        /// <summary>
        /// Creates a reader bound to a required name child (and declares the child as
        /// being <see cref="Handle(XObject)">handled</see>.
        /// </summary>
        /// <param name="name">The name of child element.</param>
        /// <param name="r">The reader. <see cref="IsValid"/> is false and should not be used if this methid returned false.</param>
        /// <returns>True if a child has been found, false otherwise.</returns>
        public bool WithOptionalChild( XName name, out XElementReader r )
        {           
            var c = Element.Element( name );
            if( c == null )
            {
                r = default;
                return false;
            }
            r = WithElement( c, true );
            return true;
        }

        /// <summary>
        /// Creates a set of readers bound to the <see cref="Element"/>'s children (and declares them as
        /// being <see cref="Handle(XObject)">handled</see>.
        /// </summary>
        /// <param name="handleElements">
        /// By default, the returned readers have not handled their respective element.
        /// When <see langword="true"/> <see cref="Handle(XObject)"/> is called with each of the chldren: all of them are handled.
        /// </param>
        /// <returns>A the set of valid readers for children.</returns>
        public IEnumerable<XElementReader> WithChildren( bool handleElements = false )
        {
            var r = this;
            return Element.Elements().Select( e => r.WithElement( e, handleElements ) );
        }

        /// <summary>
        /// Creates a set of readers bound to the named <see cref="Element"/>'s children (and declares them as
        /// being <see cref="Handle(XObject)">handled</see>.
        /// </summary>
        /// <param name="handleElements">
        /// By default, the returned readers have not handled their respective element.
        /// When <see langword="true"/> <see cref="Handle(XObject)"/> is called with each of the chldren: all of them are handled.
        /// </param>
        /// <returns>A the set of valid readers for children.</returns>
        public IEnumerable<XElementReader> WithChildren( XName name, bool handleElements = false )
        {
            var r = this;
            return Element.Elements( name ).Select( e => r.WithElement( e, handleElements ) );
        }

        /// <summary>
        /// Throws a xml exception with line/column information if possible.
        /// </summary>
        /// <param name="message">The exception message. Must not be null or empty.</param>
        public void ThrowXmlException( string message )
        {
            if( String.IsNullOrWhiteSpace( message ) ) throw new ArgumentException( nameof( message ) );
            IXmlLineInfo info = Element.GetLineColumnInfo();
            if( info.HasLineInfo() ) throw new XmlException( message + info.GetLineColumnString(), null, info.LineNumber, info.LinePosition );
            throw new XmlException( message );
        }

        /// <summary>
        /// Gets the raw <see cref="XElement"/>.
        /// Never null as long as <see cref="IsValid"/> is true.
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
        public bool Handle( XObject o )
        {
            return _handled.Add( o );
        }

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
            if( a == null ) throw new XmlException( $"Required attribute '{name}'{Element.GetLineColumnString()}." );
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
        /// The builder that knows how to instanciate a <typeparamref name="T"/>
        /// from a &lt;add ... or a &lt;remove ... element.
        /// </param>
        /// <returns>The updated item <paramref name="set"/>.</returns>
        public HashSet<T> HandleCollection<T>( XName elementName, HashSet<T> set, Func<XElementReader, T> builder )
        {
            if( elementName == null ) throw new ArgumentNullException( nameof( elementName ) );
            foreach( var c in WithChildren( elementName, handleElements: true ) )
            {
                c.HandleAddRemoveClearChildren( set, builder );
            }
            return set;
        }

        /// <summary>
        /// Handles add/remove/clear children elements of this <see cref="Element"/> (typically &lt;Things&gt;...&lt;/Things&gt;) that
        /// must be only &lt;clear /&gt; or &lt;add .../ &lt;remove ... that the <paramref name="builder"/> function can project
        /// into <typeparamref name="T"/> instances.
        /// The <see cref="HashSet{T}.Comparer"/> must be correctly configured since it is the only responsible of the duplicate
        /// handling.
        /// </summary>
        /// <typeparam name="T">Set item type.</typeparam>
        /// <param name="set">Current object set with a <see cref="HashSet{T}.Comparer"/> correctly configured.</param>
        /// <param name="builder">
        /// The builder that knows how to instanciate a <typeparamref name="T"/>
        /// from a &lt;add ... or a &lt;remove ... element.
        /// </param>
        /// <returns>The updated item <paramref name="set"/>.</returns>
        public HashSet<T> HandleAddRemoveClearChildren<T>( HashSet<T> set, Func<XElementReader, T> builder )
        {
            if( !IsValid ) throw new InvalidOperationException( nameof( IsValid ) );
            if( set == null ) throw new ArgumentNullException( nameof( set ) );
            if( builder == null ) throw new ArgumentNullException( nameof( builder ) );
            foreach( var e in Element.Elements() )
            {
                switch( e.Name.LocalName )
                {
                    case "clear": set.Clear(); _handled.Add( e ); break;
                    case "remove": set.Remove( builder( WithElement( e, true ) ) ); break;
                    case "add":
                        {
                            var item = builder( WithElement( e, true ) );
                            if( !set.Add( item ) ) Throw( e, $"Item '{item}' already exists. There must be a <remove> first." );
                            break;
                        }
                    default: Throw( e, $"Expected only <add>, <remove> or <clear/> element in '{Element.Name}'." ); break;
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
