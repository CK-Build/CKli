using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace System.Xml.Linq
{
    public static class XLinqExtension
    {
        #region Xml Beautify

        static readonly XName _xmlSpace = XName.Get( "xml", "space" );

        /// <summary>
        /// Handles insignificant white spaces.
        /// There are 4 white spaces characters: carriage return '\r', linefeed '\n', tab '\t', and spacebar (' ').
        /// This handles xml:space = "preserve" that protects any text in an element or its
        /// descendants except inside any child with xml:space="default".
        /// </summary>
        /// <param name="this">This document.</param>
        /// <param name="actualTextProcessor">
        /// Optional transformer for texts that are not entirely white spaces and for which white spaces
        /// are not preserved. The function is called with the current start line and the <see cref="StringBuilder"/>
        /// that can be changed: the function must return true if the content of the StringBuilder must be considered.
        /// </param>
        /// <returns>This document.</returns>
        public static XDocument Beautify( this XDocument @this, Func<string, StringBuilder, bool>? actualTextProcessor = null )
        {
            static bool? GetPreserve( XElement e )
            {
                var p = (string)e.Attribute( _xmlSpace );
                return p == null ? (bool?)null : p == "preserve";
            }

            const string indent = "  ";

            void Process( XElement e, bool preserve, string outerStartLine, int depth )
            {
                string startLine = outerStartLine + indent;
                if( preserve )
                {
                    foreach( var child in e.Elements() )
                    {
                        Process( child, preserve, startLine, depth + 1 );
                    }
                    return;
                }
                bool hasContent = false;
                var currentXText = new List<XText>();
                var currentText = new StringBuilder();
                XNode c = e.FirstNode;
                while( c != null )
                {
                    if( c is XText t )
                    {
                        currentXText.Add( t );
                        currentText.Append( t.Value );
                    }
                    else
                    {
                        hasContent = true;
                        Debug.Assert( c is XElement || c is XComment || c is XCData || c is XProcessingInstruction );
                        // Handling current collected XText, possibly replacing them with a unique XText.
                        if( currentXText.Count == 0 )
                        {
                            c.AddBeforeSelf( new XText( startLine ) );
                        }
                        else
                        {
                            HandleCollectedXText( actualTextProcessor, startLine, currentXText, currentText, false );
                        }
                        if( c is XElement cE )
                        {
                            Process( cE, GetPreserve( cE ) ?? preserve, startLine, depth + 1 );
                        }
                        currentXText.Clear();
                        currentText.Clear();
                    }
                    c = c.NextNode;
                }
                if( currentXText.Count == 0 )
                {
                    if( hasContent ) e.LastNode.AddAfterSelf( new XText( outerStartLine ) );
                }
                else
                {
                    HandleCollectedXText( actualTextProcessor, outerStartLine, currentXText, currentText, !hasContent );
                }
            }

            Process( @this.Root, false, Environment.NewLine, 0 );
            return @this;
        }

        static void HandleCollectedXText( Func<string, StringBuilder, bool>? actualTextProcessor,
                                          string startLine,
                                          List<XText> currentXText,
                                          StringBuilder currentText,
                                          bool removeDefaultStartLine )
        {
            bool hasActualText = false;
            Debug.Assert( currentXText.Count != 0 );
            int newLineCount = 0;
            for( int i = 0; i < currentText.Length; ++i )
            {
                char car = currentText[i];
                if( car == '\n' )
                {
                    ++newLineCount;
                }
                else if( car != '\r' && car != '\t' && car != ' ' )
                {
                    hasActualText = true;
                    break;
                }
            }
            XText? replacement = null;
            if( !hasActualText )
            {
                if( !removeDefaultStartLine )
                {
                    string newText = startLine;
                    if( newLineCount >= 2 ) newText = Environment.NewLine + newText;
                    if( currentXText.Count != 1 || currentXText[0].Value != newText )
                    {
                        replacement = new XText( newText );
                    }
                }
            }
            else
            {
                if( actualTextProcessor?.Invoke( startLine, currentText ) ?? false )
                {
                    if( currentText.Length > 0 )
                    {
                        replacement = new XText( currentText.ToString() );
                    }
                }
            }
            if( replacement != null )
            {
                currentXText[currentXText.Count - 1].AddAfterSelf( replacement );
                currentXText.Remove();
            }
            else if( !hasActualText && removeDefaultStartLine )
            {
                currentXText.Remove();
            }
        }

        #endregion

        /// <summary>
        /// Throws a <see cref="XmlException"/> with the provided <paramref name="message"/> this the line and column
        /// information if it exists.
        /// </summary>
        /// <param name="culprit">This XObject.</param>
        /// <param name="message">The message.</param>
        [DoesNotReturn]
        public static void Throw( this XObject culprit, string message )
        {
            var p = culprit.GetLineColumnInfo();
            if( p.HasLineInfo() ) throw new XmlException( message + culprit.GetLineColumnString(), null, p.LineNumber, p.LinePosition );
            throw new XmlException( message );
        }

        /// <summary>
        /// Clones any XObject into another one.
        /// </summary>
        /// <param name="this">This XObject to clone.</param>
        /// <param name="setLineColumnInfo">False to not propagate any associated <see cref="IXmlLineInfo"/> to the cloned object.</param>
        /// <returns>A clone of this object.</returns>
        [return: NotNullIfNotNull( "this" )]
        public static T? Clone<T>( this T? @this, bool setLineColumnInfo = true ) where T : XObject
        {
            XObject? o = null;
            switch( @this )
            {
                case null: return null;
                case XAttribute a: o = new XAttribute( a ); break;
                case XElement e: o = new XElement( e.Name, e.Attributes().Select( a => a.Clone() ), e.Nodes().Select( n => n.Clone() ) ); break;
                case XComment c: o = new XComment( c ); break;
                case XCData d: o = new XCData( d ); break;
                case XText t: o = new XText( t ); break;
                case XProcessingInstruction p: o = new XProcessingInstruction( p ); break;
                case XDocument d: o = new XDocument( new XDeclaration( d.Declaration ), d.Nodes().Select( n => n.Clone() ) ); break;
                case XDocumentType t: o = new XDocumentType( t ); break;
                default: throw new NotSupportedException( @this.GetType().AssemblyQualifiedName );
            }
            return setLineColumnInfo ? (T)o.SetLineColumnInfo( @this ) : (T)o;
        }

        /// <summary>
        /// Very simple process that removes any namespace: all <see cref="XElement.Name"/> are
        /// set to their respective <see cref="XName.LocalName"/> and attributes that are <see cref="XAttribute.IsNamespaceDeclaration"/>
        /// are removed.
        /// </summary>
        /// <param name="this">This element.</param>
        /// <returns>True if this element (or any of its child elements) has been changed, false otherwise.</returns>
        public static bool RemoveAllNamespaces( this XElement @this )
        {
            bool hasChanged = false;
            XName n = @this.Name;
            if( n.Namespace != XNamespace.None )
            {
                hasChanged = true;
                @this.Name = n.LocalName;
            }
            if( @this.Attributes().Any( a => a.IsNamespaceDeclaration ) )
            {
                hasChanged = true;
                @this.Attributes().Where( a => a.IsNamespaceDeclaration ).Remove();
            }
            foreach( var c in @this.Elements() )
            {
                hasChanged |= RemoveAllNamespaces( c );
            }
            return hasChanged;
        }

        /// <summary>
        /// Ensures that at least one named child element exists and returns it.
        /// </summary>
        /// <param name="this">This parent element.</param>
        /// <param name="name">The name of the element to find or create.</param>
        /// <returns>The element found or created.</returns>
        public static XElement EnsureElement( this XElement @this, XName name )
        {
            XElement? e = @this.Element( name );
            if( e == null )
            {
                e = new XElement( name );
                @this.Add( e );
            }
            return e;
        }

        /// <summary>
        /// Ensures that a named element is the first child.
        /// </summary>
        /// <param name="this">This parent element.</param>
        /// <param name="name">The element's name that must be the first one.</param>
        /// <returns>The first element that may have been moved or inserted.</returns>
        public static XElement EnsureFirstElement( this XElement @this, XName name )
        {
            XElement? e = @this.Element( name );
            if( e != null ) e.Remove();
            else e = new XElement( name );
            @this.AddFirst( e );
            return e;
        }

        /// <summary>
        /// Replaces the first element with the same name or adds a new element.
        /// </summary>
        /// <param name="this">This parent element.</param>
        /// <param name="e">The element what will replace its first homonym or be added.</param>
        /// <returns>This element.</returns>
        public static XElement ReplaceElementByName( this XElement @this, XElement e )
        {
            XElement? c = @this.Element( e.Name );
            if( c == null ) @this.Add( e );
            else c.ReplaceWith( e );
            return e;
        }

        /// <summary>
        /// Clears any <see cref="XComment"/> or <see cref="XText"/> nodes before
        /// this one.
        /// </summary>
        /// <param name="this">This element.</param>
        /// <returns>This element (fluent syntax).</returns>
        public static XElement ClearCommentsBefore( this XElement @this )
        {
            bool hasNewLine = false;
            @this.NodesBeforeSelf()
                 .Reverse()
                 .TakeWhile( n => n is XComment || n is XText )
                 .Reverse()
                 .Select( n => { hasNewLine |= n is XText t && t.Value.IndexOf( '\n' ) >= 0; return n; } )
                 .Remove();
            if( hasNewLine ) @this.AddBeforeSelf( Environment.NewLine );
            return @this;
        }

        /// <summary>
        /// Handles following <see cref="XText"/> elements and compacts them into one XText
        /// from which any leading empty lines are removed.
        /// </summary>
        /// <param name="this">This element.</param>
        /// <returns>This element (fluent syntax).</returns>
        public static XElement ClearNewLineAfter( this XElement @this )
        {
            var textElements = @this.NodesAfterSelf().TakeWhile( n => n is XText ).Cast<XText>().ToList();
            if( textElements.Count == 0 ) return @this;
            string? text = null;
            bool found = false;
            foreach( var x in textElements )
            {
                text += x.Value;
                if( found ) x.Remove();
                else found = true;
            }
            Debug.Assert( text != null, "There is at least one text element." );
            found = false;
            int i = 0;
            while( i < text.Length )
            {
                var c = text[i];
                if( c == ' ' || c == '\t' ) continue;
                if( c == '\n' )
                {
                    found = true;
                    break;
                }
                ++i;
            }
            if( found ) text = text.Substring( i + 1 );
            textElements[0].Value = text;
            return @this;
        }

        /// <summary>
        /// Calls <see cref="ClearCommentsBefore(XElement)"/> and <see cref="ClearNewLineAfter(XElement)"/>
        /// on this element.
        /// </summary>
        /// <param name="this">This element.</param>
        /// <returns>This element (fluent syntax).</returns>
        public static XElement ClearCommentsBeforeAndNewLineAfter( this XElement @this ) => @this.ClearCommentsBefore().ClearNewLineAfter();

        /// <summary>
        /// Ensures that a &lt;add key="..." value="..." /&gt; element exists
        /// based on the <paramref name="key"/>: the first existing add element
        /// with the key is updated or a new one is added.
        /// </summary>
        /// <param name="this">This element.</param>
        /// <param name="key">The key. Must not be null or empty.</param>
        /// <param name="value">The associated value. Must not be null.</param>
        public static void EnsureAddKeyValue( this XElement @this, string key, string value )
        {
            if( String.IsNullOrWhiteSpace( key ) ) throw new ArgumentNullException( nameof( key ) );
            if( value == null ) throw new ArgumentNullException( nameof( value ) );
            var f = @this.Elements( "add" ).FirstOrDefault( x => (string?)x.Attribute( "key" ) == key );
            if( f == null )
            {
                @this.Add( new XElement( "add",
                                    new XAttribute( "key", key ),
                                    new XAttribute( "value", value ) ) );
            }
            else if( (string?)f.Attribute( "value" ) != value )
            {
                f.SetAttributeValue( "value", value );
            }
        }

        internal static Dictionary<string, T> ApplyAddRemoveClear<T>( this XElement @this,
                                                                      Dictionary<string, T> map,
                                                                      Func<XElement, string> keyReader,
                                                                      Func<XElement, T> builder )
        {
            string ReadKey( XElement e )
            {
                var key = keyReader( e );
                if( key == null ) throw new Exception( $"Unable to extract key from {e}." );
                return key;
            }

            foreach( var e in @this.Elements() )
            {
                switch( e.Name.LocalName )
                {
                    case "clear": map.Clear(); break;
                    case "remove": map.Remove( ReadKey( e ) ); break;
                    case "add": map.Add( ReadKey( e ), builder( e ) ); break;
                    default: throw new Exception( $"Expected only <add>, <remove> or <clear/> element in {@this}." );
                }
            }
            return map;
        }

        internal static HashSet<T> ApplyAddRemoveClear<T>( this XElement @this,
                                                           HashSet<T> set,
                                                           Func<XElement, T> builder )
        {
            foreach( var e in @this.Elements() )
            {
                switch( e.Name.LocalName )
                {
                    case "clear": set.Clear(); break;
                    case "remove": set.Remove( builder( e ) ); break;
                    case "add":
                        {
                            var item = builder( e );
                            if( set.Contains( item ) ) throw new Exception( $"Item '{item}' already exists. There must be a <remove> first." );
                            set.Add( builder( e ) );
                            break;
                        }
                    default: throw new Exception( $"Expected only <add>, <remove> or <clear/> element in {@this}." );
                }
            }
            return set;
        }

        #region IEnumerable<XElement> ApplyAddRemoveClear extensions.
        internal static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this IEnumerable<XElement> @this,
            Func<XElement, string> keyReader,
            Func<XElement, T> builder )
        {
            return ApplyAddRemoveClear( @this, new Dictionary<string, T>(), keyReader, builder );
        }

        internal static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this IEnumerable<XElement> @this,
            Dictionary<string, T> map,
            Func<XElement, string> keyReader,
            Func<XElement, T> builder )
        {
            foreach( var e in @this )
            {
                ApplyAddRemoveClear( e, map, keyReader, builder );
            }
            return map;
        }

        internal static HashSet<T> ApplyAddRemoveClear<T>(
            this IEnumerable<XElement> @this,
            Func<XElement, T> builder,
            IEqualityComparer<T>? comparer = null )
        {
            return ApplyAddRemoveClear( @this, new HashSet<T>( comparer ), builder );
        }

        internal static HashSet<T> ApplyAddRemoveClear<T>(
            this IEnumerable<XElement> @this,
            HashSet<T> set,
            Func<XElement, T> builder )
        {
            foreach( var e in @this )
            {
                ApplyAddRemoveClear( e, set, builder );
            }
            return set;
        }
        #endregion
    }


}
