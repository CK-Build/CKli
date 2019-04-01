using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CK.IniFile
{
    class IniLine<TFormat> where TFormat : IniFormat
    {
        IniLine()
        {
        }
        protected virtual IniLine<TFormat> ParseLine( string line, TFormat format )
        {
            if( string.IsNullOrWhiteSpace( line ) )
            {
                return null;
            }
            string[] commentSplitted = line.Split( format.CommentChar.ToArray(), 2 );
            if( commentSplitted.Length == 1 ) //No comment on this line
            {
                string[] keyValue = line.Split( format.KeyDelimiter );
                if( keyValue.Length != 2 ) throw new InvalidDataException();
                return new IniLine<TFormat>( keyValue[0], keyValue[1] );
            }
            if( string.IsNullOrWhiteSpace( commentSplitted[0] ) ) //Leading comment
            {
                return new IniLine<TFormat>( commentSplitted[1] );
            }
            string[] kv = commentSplitted[0].Split( Format.KeyDelimiter );
            if( kv.Length != 2 ) throw new InvalidDataException();
            return (T)new IniLine( kv[0], kv[1], commentSplitted[1] );
        }
        string _comment;
        string _key;
        /// <summary>
        /// Create with <see cref="CommentType"/> = <see cref="IniCommentType.Leading"/>
        /// </summary>
        /// <param name="comment"></param>
        public IniLine( string comment )
        {
            CommentType = IniCommentType.Leading;
            Comment = comment;
        }
        /// <summary>
        /// Create with <see cref="CommentType"/> = <see cref="IniCommentType.None"/>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public IniLine( string key, string value )
        {
            CommentType = IniCommentType.None;
            Key = key;
            Value = value;
        }
        /// <summary>
        /// Create with <see cref="CommentType"/> = <see cref="IniCommentType.Trailing"/>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="comment"></param>
        public IniLine( string key, string value, string comment )
        {
            CommentType = IniCommentType.Trailing;
            Comment = comment;
            Key = key;
            Value = value;
        }
        public IniCommentType CommentType { get; }
        public string Comment
        {
            get => _comment;
            set
            {
                if( CommentType == IniCommentType.None ) throw new InvalidOperationException( "Can't set the comment if CommentType is None" );
                _comment = value;
            }
        }
        public string Key
        {
            get => _key;
            set
            {
                if( CommentType == IniCommentType.Leading ) throw new InvalidOperationException( "Can't set the comment if CommentType is leading" );
                _key = value;
            }
        }
        public string Value { get; set; }
    }
    enum IniCommentType
    {
        /// <summary>
        /// No comment on this line.
        /// </summary>
        None,
        /// <summary>
        /// The whole line is a comment
        /// </summary>
        Leading,
        /// <summary>
        /// There is a comment a the end of the line
        /// </summary>
        Trailing
    }
}
