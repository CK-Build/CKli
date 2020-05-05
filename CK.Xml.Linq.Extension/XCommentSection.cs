using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace System.Xml.Linq
{
    public class XCommentSection
    {
        static readonly Regex _startRegex = new Regex( "^<(?<1>[a-zA-Z]\\w*)>", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );
        readonly string _startMarker;
        readonly XComment _start;
        readonly XComment _end;

        XCommentSection( string name, string startMarker, XComment start, XComment end )
        {
            Name = name;
            _startMarker = startMarker;
            _start = start;
            _end = end;
        }

        public static XCommentSection? FindOrCreate( XElement parent, string name, bool createIfNotExists = false )
        {
            var startMarker = "<" + name + ">";
            var endMarker = "</" + name + ">";
            var start = parent.DescendantNodes().OfType<XComment>().Where( n => n.Value.StartsWith( startMarker ) ).FirstOrDefault();
            if( start == null && !createIfNotExists ) return null;
            XComment? end = null;
            if( start != null )
            {
                end = start.NodesAfterSelf().OfType<XComment>().FirstOrDefault( n => n.Value.StartsWith( endMarker ) );
                if( end == null )
                {
                    var next = start.NodesAfterSelf().OfType<XComment>().FirstOrDefault( n => _startRegex.Match( n.Value ).Success );
                    end = new XComment( endMarker );
                    if( next != null )
                    {
                        next.AddBeforeSelf( end, new XText( Environment.NewLine ) );
                    }
                    else
                    {
                        parent.Add( end, new XText( Environment.NewLine ) );
                    }
                }
            }
            else 
            {
                Debug.Assert( createIfNotExists );
                start = new XComment( startMarker );
                end = new XComment( endMarker );
                parent.Add( new XText( Environment.NewLine ), start, end, new XText( Environment.NewLine ) );
            }
            return new XCommentSection( name, startMarker, start, end );
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
        /// Gets all the nodes of this comment section.
        /// </summary>
        public IEnumerable<XNode> ContentNodes => _start.NodesAfterSelf().TakeWhile( n => n != _end );

        /// <summary>
        /// Replaces the content of this comment section.
        /// </summary>
        /// <param name="objects">Content objects.</param>
        public void SetContent( params object[] objects )
        {
            ContentNodes.Remove();
            _start.AddAfterSelf( new XText( Environment.NewLine ), objects, new XText( Environment.NewLine ) );
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
