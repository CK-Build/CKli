using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    public class XTypedObject
    {
        IReadOnlyList<XTypedObject>? _children;

        public class Initializer
        {
            /// <summary>
            /// Constructor for child objects.
            /// </summary>
            /// <param name="parent">The parent object. Never null.</param>
            /// <param name="eReader">The element reader.</param>
            /// <param name="services">The available services.</param>
            internal Initializer(
                XTypedObject parent,
                in XElementReader eReader,
                SimpleServiceContainer services )
            {
                Parent = parent;
                Reader = eReader;
                Services = services;
                ChildServices = new SimpleServiceContainer( Services );
            }

            /// <summary>
            /// Constructor for the root.
            /// </summary>
            /// <param name="root">Root factory.</param>
            /// <param name="eReader">The element reader.</param>
            /// <param name="baseProvider">Can be null.</param>
            internal Initializer( XTypedFactory root, in XElementReader eReader, IServiceProvider? baseProvider )
            {
                Reader = eReader;
                Services = new SimpleServiceContainer( baseProvider );
                Services.Add( root );
                ChildServices = new SimpleServiceContainer( Services );
            }

            /// <summary>
            /// Gets the element reader.
            /// </summary>
            public XElementReader Reader { get; }

            /// <summary>
            /// Gets the monitor to use.
            /// </summary>
            public IActivityMonitor Monitor => Reader.Monitor;

            /// <summary>
            /// Gets the raw <see cref="XElement"/>.
            /// Unfortunately, there is no read-only view of XElement, this should not be mutated
            /// otherwise an InvalidOperationException is thrown.
            /// </summary>
            public XElement Element => Reader.Element;

            /// <summary>
            /// Gets the parent typed object.
            /// </summary>
            public XTypedObject? Parent { get; }

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

        }

        /// <summary>
        /// Initializes a new <see cref="XTypedObject"/> from a <see cref="Initializer"/> that
        /// exposes the <see cref="Initializer.Element"/> and other build services such as
        /// the <see cref="Initializer.Monitor"/> to use.
        /// </summary>
        /// <param name="initializer">The initializer context.</param>
        public XTypedObject( Initializer initializer )
        {
            Parent = initializer.Parent;
            XElement = initializer.Element;
            initializer.Reader.SetPropertiesFromAttributes( this );
            XElement.AddAnnotation( this );
        }

        /// <summary>
        /// Gets the parent typed object.
        /// </summary>
        public XTypedObject? Parent { get; }

        /// <summary>
        /// Gets the first child.
        /// </summary>
        public XTypedObject? FirstChild => (_children == null || _children.Count == 0) ? null : _children[0];

        /// <summary>
        /// Gets the children typed objects.
        /// This is available right before <see cref="OnCreated(Initializer)"/> is called. 
        /// </summary>
        public IReadOnlyList<XTypedObject> Children => _children!;

        /// <summary>
        /// Enumerates through all descendants of this XTypedObject, returning the topmost
        /// XTypedObject that match the <paramref name="predicate"/>.
        /// </summary>
        /// <param name="predicate">Filter condition: when returning false, the object and its descendants are skipped.</param>
        /// <returns>The set of descendants that match the predicate in document order regardless of their depth.</returns>
        /// <param name="withSelf">
        /// True to consider <paramref name="this"/> element. Defaults to consider only the children elements.
        /// When true and this <see cref="XTypedObject"/> satisfies the predicate, this is the single element returned.
        /// </param>
        public IEnumerable<XTypedObject> TopDescendants( Func<XTypedObject, bool> predicate, bool withSelf = false )
        {
            Throw.CheckNotNullArgument( predicate );
            if( withSelf && predicate( this ) )
            {
                yield return this;
                yield break;
            }
            var current = FirstChild;
            while( current != null )
            {
                XTypedObject? next = null;
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
        /// Enumerates through all descendants of this XTypedObject, returning the topmost
        /// <see cref="XTypedObject"/> that is a <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type to select.</typeparam>
        /// <param name="withSelf">
        /// True to consider <paramref name="this"/> element. Defaults to consider only the children elements.
        /// When true and this <see cref="XTypedObject"/> is a <typeparamref name="T"/>, this is the single element returned.
        /// </param>
        /// <returns>The top <typeparamref name="T"/> elements below this one or this one.</returns>
        public IEnumerable<T> TopDescendants<T>( bool withSelf = false ) where T : XTypedObject
        {
            return TopDescendants( x => x is T, withSelf ).Cast<T>();
        }

        /// <summary>
        /// Gets all the descendants of a given type.
        /// </summary>
        /// <typeparam name="T">Type of the descendants that must be enumerated.</typeparam>
        /// <param name="breadthFirst">
        /// False to enumerate the nodes in depth-first order rather than breadth-first.
        /// </param>
        /// <param name="withSelf">
        /// True to consider <paramref name="this"/> element. Defaults to consider only the children elements.
        /// </param>
        /// <returns>The set of typed descendants.</returns>
        public IEnumerable<T> Descendants<T>( bool breadthFirst = true, bool withSelf = false )
        {
            if( breadthFirst && withSelf && this is T tTb ) yield return tTb;
            foreach( var c in Children )
            {
                foreach( var d in c.Descendants<T>( breadthFirst, true ) ) yield return d;
            }
            if( !breadthFirst && withSelf && this is T tTd ) yield return tTd;
        }

        /// <summary>
        /// Gets the next sibling.
        /// Null if this is the last children of the <see cref="Parent"/>.
        /// </summary>
        public XTypedObject? NextSibling { get; private set; }

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
        /// Unfortunately, there is no read-only view of XElement, so the check is at runtime:
        /// this should not be mutated otherwise an InvalidOperationException is thrown.
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

        internal bool OnChildrenCreated( Initializer initializer, IReadOnlyList<XTypedObject> children )
        {
            XTypedObject? sibling = null;
            for( int i = children.Count - 1; i >= 0; --i )
            {
                var c = children[i];
                c.NextSibling = sibling;
                sibling = c;
            }
            _children = children;
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
        /// <returns>Must return true on success, false if an error occurred.</returns>
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
