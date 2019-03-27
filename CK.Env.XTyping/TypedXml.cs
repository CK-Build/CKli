using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Env
{
    public sealed class TypedXml
    {
        TypedXml( XElement e, Type t, List<TypedXml> children, List<XElement> configs )
        {
            Type = t;
            Element = e;
            e.AddAnnotation( this );
            TypedChildren = children;
            UnTypedChildren = configs;
         }

        /// <summary>
        /// Gets the Xml element.
        /// </summary>
        public XElement Element { get; }

        /// <summary>
        /// Gets the type associated with this <see cref="Element"/>.
        /// Can be null.
        /// </summary>
        public Type Type { get; }

        /// <summary>
        /// Gets the first <see cref="TypedXml"/> children regardless of their depth.
        /// </summary>
        public IReadOnlyList<TypedXml> TypedChildren { get; }

        /// <summary>
        /// Gets the direct child of this <see cref="Element"/> that are not associated to a type:
        /// they are considered as configuration data.
        /// </summary>
        public IReadOnlyList<XElement> UnTypedChildren { get; }

        /// <summary>
        /// Creates a <see cref="TypedXml"/> associated
        /// to a Xml element or throws an <see cref="InvalidOperationException"/> if
        /// if no mapped type exists for the root element name.
        /// </summary>
        /// <param name="e">The Xml element.</param>
        /// <param name="mapper">The function that associates an Xml element to a type.</param>
        /// <returns>The TypedXml object.</returns>
        public static TypedXml Create( XElement e, Func<XElement, Type> mapper, Type rootType = null )
        {
            if( e == null ) throw new ArgumentNullException( nameof( e ) );
            if( mapper == null ) throw new ArgumentNullException( nameof( mapper ) );
            if( rootType == null )
            {
                rootType = mapper( e );
                if( rootType == null ) throw new InvalidOperationException( $"No type registered for root element '{e.Name}'." );
            }
            return DoEnsureTypedXml( e, rootType, mapper );
        }

        static TypedXml DoEnsureTypedXml( XElement e, Type known, Func<XElement, Type> mapper )
        {
            if( known == null && (known = mapper( e )) == null ) return null;
            var untyped = new List<XElement>();
            var typed = new List<TypedXml>();
            foreach( var c in e.Elements() )
            {
                var cT = DoEnsureTypedXml( c, null, mapper );
                if( cT != null ) typed.Add( cT );
                else untyped.Add( c );
            }
            return new TypedXml( e, known, typed, untyped );
        }
    }
}
