using System;
using System.Collections.Generic;
using System.Text;

namespace CK.IniFile.IniITems
{
    /// <summary>
    /// Represents a base class for INI content items, <see cref="IniSection"/> and <see cref="IniKey"/>.
    /// </summary>
    /// <remarks>
    /// <para>All INI items share the same content like <see cref="Name"/>, <see cref="LeadingComment"/> and <see cref="TrailingComment"/>.
    /// These properties are defined on an <see cref="IniItem"/> class, a base class for INI content items.</para>
    /// </remarks>
    public abstract class IniItem
    {
        private string _name;
        private readonly IniFile _parentFile;
        private IniComment _leadingComment;
        private IniComment _trailingComment;

        /// <summary>
        /// Gets and sets the name of the current <see cref="IniItem"/>.
        /// </summary>
        /// <remarks>
        /// When setting <see cref="IniItem.Name"/> the value is verified by the item's <see cref="IniDuplication"/> rule.
        /// </remarks>
        public string Name
        {
            get { return _name; }
            set
            {
                if( ParentCollectionCore == null || ((IItemNameVerifier)ParentCollectionCore).VerifyItemName( value ) )
                    _name = value;
            }
        }

        /// <summary>
        /// Gets or sets the amount of whitespace characters before this <see cref="IniItem.Name">item's name</see>.
        /// </summary>
        public int LeftIndentation { get; set; }

        /// <summary>
        /// Gets the <see cref="IniComment"/> object that represents a comment that follows this <see cref="IniItem"/> on the same line.
        /// </summary>
        public IniComment LeadingComment
        {
            get
            {
                if( _leadingComment == null )
                    _leadingComment = new IniComment( IniCommentType.Leading );
                return _leadingComment;
            }
        }

        internal bool HasLeadingComment { get { return _leadingComment != null; } }

        /// <summary>
        /// Gets the <see cref="IniComment"/> object that represents a comments that occur before this <see cref="IniItem"/>.
        /// </summary>
        public IniComment TrailingComment
        {
            get
            {
                if( _trailingComment == null )
                    _trailingComment = new IniComment( IniCommentType.Trailing );
                return _trailingComment;
            }
        }

        internal bool HasTrailingComment { get { return _trailingComment != null; } }

        /// <summary>
        /// Gets the <see cref="IniFile"/> to which this <see cref="IniItem"/> belongs to.
        /// </summary>
        public IniFile ParentFile { get { return _parentFile; } }

        internal object ParentCollectionCore { get; set; }

        internal IniItem( IniFile parentFile, string name, IniComment trailingComment = null )
        {
            _name = name ?? throw new ArgumentNullException( "name" );
            _parentFile = parentFile ?? throw new ArgumentNullException( "parentFile" );
            _trailingComment = trailingComment;
        }

        /// <summary>
        /// Deep copy constructor.
        /// </summary>
        /// <param name="parentFile"></param>
        /// <param name="sourceItem"></param>
        internal IniItem( IniFile parentFile, IniItem sourceItem )
        {
            _name = sourceItem._name;
            _parentFile = parentFile ?? throw new ArgumentNullException( "parentFile" );
            if( sourceItem.HasLeadingComment )
                _leadingComment = new IniComment( sourceItem._leadingComment );
            if( sourceItem.HasTrailingComment )
                _trailingComment = new IniComment( sourceItem._trailingComment );
        }
    }
}
