using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace System.Xml.Linq
{
    public static class XLinqExtension
    {
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
