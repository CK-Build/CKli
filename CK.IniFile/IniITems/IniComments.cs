using System;
using System.Collections.Generic;
using System.Text;

namespace CK.IniFile.IniITems
{
    /// <summary>
    /// Represents a comment object used by <see cref="IniItem"/> objects, <see cref="IniSection"/> and <see cref="IniKey"/>.
    /// </summary>
    public sealed class IniComment
    {
        private string _text;
        private readonly IniCommentType _type;

        /// <summary>
        /// Gets or sets the amount of empty lines before this <see cref="IniComment.Text">comment's text</see>.
        /// </summary>
        public int EmptyLinesBefore { get; set; }

        /// <summary>
        /// Gets or sets the amount of whitespace characters before this <see cref="IniComment.Text">comment's text</see>.
        /// </summary>
        public int LeftIndentation { get; set; }

        /// <summary>
        /// Gets or sets a text of this <see cref="IniComment"/> instance.
        /// </summary>
        /// <remarks>
        /// <para>For <see cref="IniItem.LeadingComment">LeadingComment</see> text should not contain new line characters.
        /// If it does, they will be replaced with a space characters.</para>
        /// </remarks>
        public string Text
        {
            get { return _text; }
            set
            {
                if( value != null && _type == IniCommentType.Leading )
                {
                    _text = value.Replace( "\r\n", " " )
                                     .Replace( "\n", " " )
                                     .Replace( "\r", " " );
                }
                else
                    _text = value;
            }
        }

        internal IniComment( IniCommentType type ) { _type = type; }

        // Deep copy constructor.
        internal IniComment( IniComment source )
        {
            _text = source._text;
            _type = source._type;
            EmptyLinesBefore = source.EmptyLinesBefore;
            LeftIndentation = source.LeftIndentation;
        }
    }
}
