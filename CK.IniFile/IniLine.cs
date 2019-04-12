using System;

namespace CK.IniFile
{
    public class IniLine
    {
        /// <summary>
        /// Deep copy constructor
        /// </summary>
        /// <param name="iniLine"></param>
        protected IniLine( IniLine iniLine )
        {
            Comment = iniLine.Comment;
            CommentType = iniLine.CommentType;
            Key = iniLine.Key;
            Value = iniLine.Value;
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

        public virtual string ToString<TFormat, TLine>( TFormat iniFormat )
                                where TFormat : IniFormat<TLine>
                                where TLine : IniLine
        {
            switch( CommentType )
            {
                case IniCommentType.Leading:
                    return iniFormat.CommentChar[0] + Comment;
                case IniCommentType.None:
                    return Key + iniFormat.KeyDelimiter + Value;
                case IniCommentType.Trailing:
                    return Key + iniFormat.KeyDelimiter + Value + " " + iniFormat.CommentChar[0] + Comment;
                default:
                    throw new InvalidOperationException();
            }
        }

        public IniCommentType CommentType { get; }

        public string Comment
        {
            get => _comment;
            set
            {
                if( !String.IsNullOrEmpty( value ) )
                {
                    if( CommentType == IniCommentType.None ) throw new InvalidOperationException( "Can't set the comment if CommentType is None" );
                    _comment = value;
                }
            }
        }

        public string Key
        {
            get => _key;
            set
            {
                if( value != null && CommentType == IniCommentType.Leading ) throw new InvalidOperationException( "Can't set the key if CommentType is leading" );
                _key = value;
            }
        }
        public string Value { get; set; }
    }

    public enum IniCommentType
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
