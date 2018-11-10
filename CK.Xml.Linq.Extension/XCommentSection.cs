using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Xml.Linq
{
    public class XCommentSection
    {
        readonly string _startMarker;
        readonly string _endMarker;
        readonly XComment _start;
        readonly XComment _end;

        XCommentSection( string name, string startMarker, string endMarker, XComment start, XComment end )
        {
            Name = name;
            _startMarker = startMarker;
            _endMarker = endMarker;
            _start = start;
            _end = end;
        }

        public static XCommentSection FindOrCreate( XElement parent, string name, bool createIfNotExists = false )
        {
            var startMarker = "<" + name + ">";
            var endMarker = "</" + name + ">";
            var start = parent.DescendantNodes().OfType<XComment>().Where( n => n.Value.StartsWith( startMarker ) ).SingleOrDefault();
            XComment end = null;
            if( start != null )
            {
                end = start.NodesAfterSelf().OfType<XComment>().Where( n => n.Value.StartsWith( endMarker ) ).SingleOrDefault();
                if( end == null ) throw new Exception( $"Invalid Comment Section: Unable to find closing <!--{endMarker}--> in '{parent}'." );
            }
            else if( createIfNotExists )
            {
                start = new XComment( startMarker );
                end = new XComment( endMarker );
                parent.Add( start, end );
            }
            return start != null ? new XCommentSection( name, startMarker, endMarker, start, end ) : null;
        }

        public string Name { get; }

        /// <summary>
        /// Gets or sets the start comment.
        /// </summary>
        public string StartComment
        {
            get { return _start.Value.Substring( _startMarker.Length ); }
            set { _start.Value = _startMarker + value; }
        }

        /// <summary>
        /// Removes this comment section from its parent.
        /// </summary>
        public void Remove()
        {
            ClearContent();
            _start.Remove();
            _end.Remove();
        }

        /// <summary>
        /// Gets the content of this comment section.
        /// </summary>
        public IEnumerable<XElement> Content => _start.NodesAfterSelf().TakeWhile( n => n != _end ).OfType<XElement>();

        /// <summary>
        /// Replaces the content of this comment section.
        /// </summary>
        /// <param name="objects">Content objects.</param>
        public void SetContent( params object[] objects )
        {
            Content.Remove();
            _start.AddAfterSelf( objects );
        }

        /// <summary>
        /// Clears the content of this comment section.
        /// </summary>
        public void ClearContent()
        {
            Content.Remove();
        }

    }

}
