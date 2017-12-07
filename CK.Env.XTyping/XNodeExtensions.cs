using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    public static class XNodeExtensions
    {
        public static string ToStringPath( this XElement @this )
        {
            return @this.AncestorsAndSelf().Reverse().Select( e => e.Name.ToString() ).Concatenate( "/" );
        }

        /// <summary>
        /// Enumerates through all descendants of the given element, returning the topmost
        /// elements that match the given predicate
        /// </summary>
        /// <param name="this">This element.</param>
        /// <param name="predicate">Filter condition. When successful, children nodes are skipped.</param>
        /// <returns>The set of descendants that match the predicate in document order regardless of their depth.</returns>
        /// <param name="withSelf">True to consider <paramref name="this"/> element. Defaults to consider only the element children.</param>
        public static IEnumerable<XElement> TopDescendantsWhere( this XElement @this, Func<XElement, bool> predicate, bool withSelf = false )
        {
            if( predicate == null ) throw new ArgumentNullException( nameof(predicate) );
            return GetDescendantsUntil( @this, predicate, withSelf );
        }

        static IEnumerable<XElement> GetDescendantsUntil( XElement root, Func<XElement, bool> predicate, bool includeSelf )
        {
            if( root == null ) yield break;
            if( includeSelf && predicate( root ) )
            {
                yield return root;
                yield break;
            }
            var current = root.FirstChild<XElement>();
            while( current != null )
            {
                XElement next = null;
                if( predicate( current ) ) yield return current;
                else
                {
                    // Dive into the children (if any).
                    next = current.FirstChild<XElement>();
                }
                // If current matched or has no children, next is the next sibling.
                if( next == null ) next = current.NextSibling<XElement>();

                // No more siblings: walk up the parents until one has as sibling or is the root.
                if( next == null )
                {
                    var parent = current.Parent as XElement;
                    while( parent != null && parent != root )
                    {
                        if( (next = parent.NextSibling<XElement>()) != null ) break;
                        parent = parent.Parent as XElement;
                    }
                }
                current = next;
            }
        }

        /// <summary>
        /// Gets the first typed child.
        /// </summary>
        /// <typeparam name="TNode">Type of the node.</typeparam>
        /// <param name="this">This node.</param>
        /// <returns>The first <typeparamref name="TNode"/> child or null if none.</returns>
        public static TNode FirstChild<TNode>( this XNode @this ) where TNode : XNode
        {
            var container = @this as XContainer;
            return container?.FirstNode as TNode;
        }

        /// <summary>
        /// Gets the next typed sibling.
        /// </summary>
        /// <typeparam name="TNode">Type of the node.</typeparam>
        /// <param name="this">This node.</param>
        /// <returns>The next <typeparamref name="TNode"/> sibling or null if none.</returns>
        public static TNode NextSibling<TNode>( this XNode @this ) where TNode : XNode
        {
            @this = @this?.NextNode;
            while( @this != null )
            {
                var n = @this as TNode;
                if( n != null ) return n;
                @this = @this.NextNode;
            }
            return null;
        }
    }
}
