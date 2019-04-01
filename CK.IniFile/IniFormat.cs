using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CK.IniFile
{
    public class IniFormat<TLine>
        where TLine : IniLine
    {
        protected IniLine InternalParse( string line )
        {
            if( string.IsNullOrWhiteSpace( line ) )
            {
                return null;
            }
            string[] commentSplitted = line.Split( CommentChar.ToArray(), 2 );
            if( commentSplitted.Length == 1 ) //No comment on this line
            {
                string[] keyValue = line.Split( KeyDelimiter );
                if( keyValue.Length != 2 ) throw new InvalidDataException();
                return new IniLine( keyValue[0], keyValue[1] );
            }
            if( string.IsNullOrWhiteSpace( commentSplitted[0] ) ) //Leading comment
            {
                return new IniLine( commentSplitted[1] );
            }
            string[] kv = commentSplitted[0].Split( KeyDelimiter );
            if( kv.Length != 2 ) throw new InvalidDataException();
            return new IniLine( kv[0], kv[1], commentSplitted[1] );
        }

        internal virtual TLine ParseLine( string line )
        {
            return (TLine)InternalParse( line );
        }
        public List<char> CommentChar { get; protected set; }
        public bool SupportSection => SectionWrapper?.Any() ?? false;
        public char KeyDelimiter { get; protected set; }
        public List<(char, char)> SectionWrapper { get; protected set; }
        public IniDuplication Duplication { get; protected set; }
        
    }
    public enum IniDuplication
    {
        /// <summary>
        /// Allow duplicates keys
        /// </summary>
        Allowed = 0,
        /// <summary>
        /// Disallow duplicate keys.
        /// </summary>
        /// <remarks>
        /// <see cref="InvalidOperationException"/> is thrown on duplicate name occurence.
        /// </remarks>
        Disallowed,
        /// <summary>
        /// Ignore duplicate names.
        /// </summary>
        Ignored
    }
}
