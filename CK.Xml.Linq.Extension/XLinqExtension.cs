using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace System.Xml.Linq
{
    public static class XLinqExtension
    {
        static readonly XName _xmlSpace = XName.Get( "xml", "space" );

        /// <summary>
        /// Handles insignificant white spaces.
        /// There are 4 white spaces characters: carriage return '\r', linefeed '\n', tab '\t', and spacebar (' ').
        /// This handles xml:space = "preserve" that protects any text in an element or its
        /// descendants except inside any child with xml:space="default".
        /// </summary>
        /// <param name="this">This document.</param>
        /// <param name="actualTextProcessor">
        /// Optional transformer for texts that are not entirely white spaces and white spaces are not preserved.
        /// The function is called with the current start line and the <see cref="StringBuilder"/> that can be
        /// changed and must return true if the content of the StringBuilder must be considered.
        /// </param>
        /// <returns>This document.</returns>
        public static XDocument Beautify( this XDocument @this, Func<string, StringBuilder, bool> actualTextProcessor = null )
        {
            bool? GetPreserve( XElement e )
            {
                var p = (string)e.Attribute( _xmlSpace );
                return p == null ? (bool?)null : p == "preserve";
            }

            string indent = "  ";

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

        static void HandleCollectedXText(
            Func<string, StringBuilder, bool> actualTextProcessor,
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
                if( car == '\n' ) ++newLineCount;
                else if( car != '\r' && car != '\t' && car != ' ' )
                {
                    hasActualText = true;
                    break;
                }
            }
            XText replacement = null;
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

        public static void RemoveAllNamespaces( this XElement @this )
        {
            @this.Name = @this.Name.LocalName;

            foreach( var c in @this.Elements() )
            {
                RemoveAllNamespaces( c );
            }
        }

        public static XElement EnsureElement( this XElement @this, XName name )
        {
            XElement e = @this.Element( name );
            if( e == null )
            {
                e = new XElement( name );
                @this.Add( e );
            }
            return e;
        }

        public static XElement EnsureFirstElement( this XElement @this, XName name )
        {
            XElement e = @this.Element( name );
            if( e != null ) e.Remove();
            else e = new XElement( name );
            @this.AddFirst( e );
            return e;
        }

        public static XElement RemoveCommentsBefore( this XElement @this )
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
            var f = @this.Elements( "add" ).FirstOrDefault( x => (string)x.Attribute( "key" ) == key );
            if( f == null )
            {
                @this.Add( new XElement( "add",
                                    new XAttribute( "key", key ),
                                    new XAttribute( "value", value ) ) );
            }
            else if( (string)f.Attribute( "value" ) != value )
            {
                f.SetAttributeValue( "value", value );
            }
        }


        #region ApplyAddRemoveClear on Dictionary whith a Func<XElement, string> keyReader.

        public static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this XElement @this,
            Func<XElement, string> keyReader,
            Func<XElement, T> builder )
        {
            return ApplyAddRemoveClear( @this, new Dictionary<string, T>(), keyReader, builder );
        }

        public static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this XElement @this,
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

        #endregion

        #region ApplyAddRemoveClear on Dictionary<string,T> whith a Func<T, string> keySelector.

        public static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this XElement @this,
            Func<XElement, T> builder,
            Func<T, string> keySelector)
        {
            return ApplyAddRemoveClear(@this, new Dictionary<string, T>(), builder, keySelector);
        }

        public static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this XElement @this,
            Dictionary<string, T> map,
            Func<XElement, T> builder,
            Func<T, string> keySelector)
        {
            foreach( var e in @this.Elements() )
            {
                switch( e.Name.LocalName )
                {
                    case "clear": map.Clear(); break;
                    case "remove": map.Remove( keySelector( builder( e ) ) ); break;
                    case "add":
                        {
                            var item = builder( e );
                            map.Add( keySelector( item ), item );
                            break;
                        }
                    default: throw new Exception( $"Expected only <add>, <remove> or <clear/> element in {@this}." );
                }
            }
            return map;
        }

        #endregion

        public static HashSet<T> ApplyAddRemoveClear<T>(
            this XElement @this,
            Func<XElement, T> builder,
            IEqualityComparer<T> comparer = null )
        {
            return ApplyAddRemoveClear( @this, new HashSet<T>( comparer ), builder );
        }


        public static HashSet<T> ApplyAddRemoveClear<T>(
            this XElement @this,
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
        public static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this IEnumerable<XElement> @this,
            Func<XElement, string> keyReader,
            Func<XElement, T> builder )
        {
            return ApplyAddRemoveClear( @this, new Dictionary<string, T>(), keyReader, builder );
        }

        public static Dictionary<string, T> ApplyAddRemoveClear<T>(
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

        public static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this IEnumerable<XElement> @this,
            Func<XElement, T> builder,
            Func<T, string> keySelector)
        {
            return ApplyAddRemoveClear(@this, new Dictionary<string, T>(), builder, keySelector);
        }

        public static Dictionary<string, T> ApplyAddRemoveClear<T>(
            this IEnumerable<XElement> @this,
            Dictionary<string, T> map,
            Func<XElement, T> builder,
            Func<T, string> keySelector)
        {
            foreach( var e in @this )
            {
                ApplyAddRemoveClear(e, map, builder, keySelector);
            }
            return map;
        }

        public static HashSet<T> ApplyAddRemoveClear<T>(
            this IEnumerable<XElement> @this,
            Func<XElement, T> builder,
            IEqualityComparer<T> comparer = null )
        {
            return ApplyAddRemoveClear( @this, new HashSet<T>( comparer ), builder );
        }

        public static HashSet<T> ApplyAddRemoveClear<T>(
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
