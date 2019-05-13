using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Env
{
    public class XTypedObject
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

            /// <summary>
            /// Gets the service container for siblings and children elements.
            /// Service added to this container will be available to siblings and children.
            /// </summary>
            public ISimpleServiceContainer Services { get; }

            /// <summary>
            /// Gets the service container for children elements only.
            /// Service added to this container will be available only to children.
            /// </summary>
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

        public XTypedObject( Initializer initializer )
        {
            Parent = initializer.Parent;
            XElement = initializer.Element;
            var a = initializer.Element.FirstAttribute;
            while( a != null )
            {
                var pInh = GetType().GetProperty( a.Name.LocalName );
                var p = pInh?.DeclaringType.GetProperty( pInh.Name );
                if( p != null && p.CanWrite )
                {
                    object v;
                    if( p.PropertyType == typeof( NormalizedPath ) )
                    {
                        v = new NormalizedPath( a.Value );
                    }
                    else if( p.PropertyType == typeof( Uri ) )
                    {
                        v = new Uri( a.Value );
                    }
                    else if( p.PropertyType.IsEnum )
                    {
                        v = Enum.Parse( p.PropertyType, a.Value );
                    }
                    else
                    {
                        v = Convert.ChangeType( a.Value, p.PropertyType );
                    }

                    p.SetValue( this, v );
                }
                a = a.NextAttribute;
            }
            XElement.AddAnnotation( this );
        }

        /// <summary>
        /// Gets the parent typed object.
        /// </summary>
        public XTypedObject Parent { get; }

        /// <summary>
        /// Gets the first child.
        /// </summary>
        public XTypedObject FirstChild => Children?.Count == 0 ? null : Children[0];

        /// <summary>
        /// Gets the children typed objects.
        /// This is available right before <see cref="OnCreated(Initializer)"/> is called. 
        /// </summary>
        public IReadOnlyList<XTypedObject> Children { get; private set; }

        /// <summary>
        /// Enumerates through all descendants of the given element, returning the topmost
        /// elements that match the given predicate
        /// </summary>
        /// <param name="predicate">Filter condition. When successful, children nodes are skipped.</param>
        /// <returns>The set of descendants that match the predicate in document order regardless of their depth.</returns>
        /// <param name="withSelf">True to consider <paramref name="this"/> element. Defaults to consider only the element children.</param>
        public IEnumerable<XTypedObject> TopDescendants( Func<XTypedObject, bool> predicate, bool withSelf = false )
        {
            if( predicate == null ) throw new ArgumentNullException( nameof( predicate ) );
            if( withSelf && predicate( this ) )
            {
                yield return this;
                yield break;
            }
            var current = FirstChild;
            while( current != null )
            {
                XTypedObject next = null;
                if( predicate( current ) )
                {
                    yield return current;
                }
                else
                {
                    // Dive into the children (if any).
                    next = current.FirstChild;
                }
                // If current matched or has no children, next is the next sibling.
                if( next == null ) next = current.NextSibling;

                // No more siblings: walk up the parents until one has as sibling or is the root.
                if( next == null )
                {
                    var parent = current.Parent;
                    while( parent != null && parent != this )
                    {
                        if( (next = parent.NextSibling) != null ) break;
                        parent = parent.Parent;
                    }
                }
                current = next;
            }
        }


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
                sibling = c;
            }
            Children = children;
            foreach( var c in children )
            {
                c.OnSiblingsCreated( initializer.Monitor );
            }
            return OnCreated( initializer );
        }

        /// <summary>
        /// Called once <see cref="NextSibling"/> is available (and all the siblings up to the last child
        /// of the <see cref="Parent"/>.
        /// This default implementation simply returns true.
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
