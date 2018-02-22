using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public abstract class XTypedObject
    {
        public class Initializer
        {
            Dictionary<object, object> _initializationState;

            internal Initializer( IActivityMonitor monitor, XTypedObject parent, TypedXml e, SimpleServiceContainer services )
            {
                TypedXml = e;
                Parent = parent;
                Monitor = monitor;
                Services = services;
                ChildServices = new SimpleServiceContainer( Services );
            }

            internal Initializer( IActivityMonitor monitor, TypedXml e, IServiceProvider baseProvider )
            {
                TypedXml = e;
                Monitor = monitor;
                Services = new SimpleServiceContainer( baseProvider );
                ChildServices = new SimpleServiceContainer( Services );
            }

            internal TypedXml TypedXml { get; }

            public IActivityMonitor Monitor { get; }

            public XElement Element => TypedXml.Element;

            public XTypedObject Parent { get; }

            public ISimpleServiceContainer Services { get; }

            public ISimpleServiceContainer ChildServices { get; }

            public IDictionary<object, object> InitializationState
            {
                get
                {
                    if( _initializationState == null )
                    {
                        _initializationState = new Dictionary<object, object>();
                    }
                    return _initializationState;
                }
            }

        }

        protected XTypedObject( Initializer initializer )
        {
            Parent = initializer.Parent;
            XElement = initializer.Element;
            var a = initializer.Element.FirstAttribute;
            while( a != null )
            {
                var p = GetType().GetProperty( a.Name.LocalName );
                if( p != null && p.CanWrite )
                {
                    object v;
                    if( p.PropertyType == typeof(NormalizedPath))
                    {
                        v = new NormalizedPath( a.Value );
                    }
                    else v = Convert.ChangeType( a.Value, p.PropertyType );
                    p.SetValue( this, v );
                }
                a = a.NextAttribute;
            }
        }

        /// <summary>
        /// Gets the parent typed object.
        /// </summary>
        public XTypedObject Parent { get; }

        /// <summary>
        /// Gets the children typed objects.
        /// This is available right before <see cref="OnCreated(Initializer)"/> is called. 
        /// </summary>
        public IReadOnlyList<XTypedObject> Children { get; private set; }

        /// <summary>
        /// Gets the next sibling.
        /// Null if this is the last children of the <see cref="Parent"/>.
        /// </summary>
        public XTypedObject NextSibling { get; private set; }

        /// <summary>
        /// Gets the next siblings.
        /// </summary>
        public IEnumerable<XTypedObject> NextSiblings
        {
            get
            {
                var s = NextSibling;
                while( s != null )
                {
                    yield return s;
                    s = s.NextSibling;
                }
            }
        }

        /// <summary>
        /// Gets the raw <see cref="XElement"/>.
        /// Unfortunaltely, there is no read-only view of XElement, this should not be mutated
        /// otherwise an InvalidOperationException is thrown.
        /// </summary>
        public XElement XElement { get; }

        /// <summary>
        /// Called once the <see cref="Children"/> are available.
        /// Does nothing at this level (returns true).
        /// </summary>
        /// <param name="initializer">The object intializer.</param>
        /// <returns>True on success, false on error (errors must be logged into <see cref="Initializer.Monitor"/>).</returns>
        protected virtual bool OnCreated( Initializer initializer )
        {
            return true;
        }

        /// <summary>
        /// Gets all the descendants of a given type.
        /// </summary>
        /// <typeparam name="T">Type of the descendants that must be enumerated.</typeparam>
        /// <returns>The set of typed descendants.</returns>
        public IEnumerable<T> Descendants<T>()
        {
            foreach( var c in Children )
            {
                if( c is T tC ) yield return tC;
                foreach( var d in c.Descendants<T>() )
                {
                    if( d is T tD ) yield return tD;
                }
            }
        }

        internal bool OnChildrenCreated( Initializer initializer, IReadOnlyList<XTypedObject> children )
        {
            XTypedObject sibling = null;
            for( int i = children.Count-1; i >= 0; --i )
            {
                var c = children[i];
                c.NextSibling = sibling;
                c.OnSiblingsCreated( initializer.Monitor );
                sibling = c;
            }
            Children = children;
            return OnCreated( initializer );
        }

        /// <summary>
        /// Called once <see cref="NextSibling"/> is available (and all the siblings up to the last child
        /// of the <see cref="Parent"/>.
        /// </summary>
        /// <param name="monitor">Monitor that must be used to log any information.</param>
        /// <returns>Must return true on success, false if an error occured.</returns>
        protected virtual bool OnSiblingsCreated( IActivityMonitor monitor )
        {
            return true;
        }

        /// <summary>
        /// Overridden to return the XElement name.
        /// </summary>
        /// <returns>The XElement name.</returns>
        public override string ToString() => XElement.Name.LocalName;
    }
}
