using System;
using System.Collections.Generic;
using System.Text;

namespace CK.IniFile
{
    /// <summary>
    /// Represents a class that defines INI file's format, stores properties used for both reading and writing a file.
    /// </summary>
    /// <remarks>
    /// <para>After an instance of this class is passed to an <see cref="IniFile"/> constructor, further changes on that instance's properties will have no effect.</para>
    /// <list type="table">
    /// <listheader>
    /// <term>Property</term>
    /// <description>Default Value</description>
    /// </listheader>
    /// <item>
    /// <term><see cref="IniOptions.CommentStarter">CommentStarter</see></term>
    /// <description><see cref="IniCommentStarter.Semicolon"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.CommentStarter">Compression</see></term>
    /// <description><see langword="false"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.CommentStarter">EncryptionPassword</see></term>
    /// <description><see langword="null"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.Encoding">Encoding</see></term>
    /// <description><see cref="System.Text.Encoding.ASCII">Encoding.ASCII</see></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.KeyDelimiter">KeyDelimiter</see></term>
    /// <description><see cref="IniKeyDelimiter.Equal"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.KeyDuplicate">KeyDuplicate</see></term>
    /// <description><see cref="IniEnums.Allowed"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.KeyNameCaseSensitive">KeyNameCaseSensitive</see></term>
    /// <description><see langword="false"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.KeySpaceAroundDelimiter">KeySpaceAroundDelimiter</see></term>
    /// <description><see langword="false"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.SectionDuplicate">SectionDuplicate</see></term>
    /// <description><see cref="IniEnums.Allowed"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.SectionNameCaseSensitive">SectionNameCaseSensitive</see></term>
    /// <description><see langword="false"/></description>
    /// </item>
    /// <item>
    /// <term><see cref="IniOptions.SectionWrapper">SectionWrapper</see></term>
    /// <description><see cref="IniSectionWrapper.SquareBrackets"/></description>
    /// </item>
    /// </list>
    /// </remarks>
    public sealed class IniOptions
    {
        private Encoding _encoding;
        private Tuple<char,char> _sectionWrapper;
        internal char _sectionWrapperStart;
        internal char _sectionWrapperEnd;

        /// <summary>
        /// Gets or sets encoding for reading and writing an INI file.
        /// </summary>
        /// <remarks>
        /// Value should not be <see langword="null"/>, if it is then a default <see cref="System.Text.Encoding.ASCII">Encoding.ASCII</see> value will be used.
        /// </remarks>
        public Encoding Encoding
        {
            get { return _encoding; }
            set
            {
                if( value == null )
                    _encoding = Encoding.UTF8;
                _encoding = value;
            }
        }

        /// <summary>
        /// Gets or sets comments starting character.
        /// </summary>
        public List<char> CommentStarter { get; set; } = new List<char>();

        /// <summary>
        /// <para>Gets or sets a value indicating if file's size is reduced.</para>
        /// <para>If <see langword="true"/> file is decompressed on Load and compressed on Save.</para>
        /// </summary>
        public bool Compression { get; set; }

        /// <summary>
        /// <para>Gets or sets an INI file's protection password.</para>
        /// <para>File is decrypted on Load and encrypted on Save if a password is not <see langword="null"/> or <see cref="System.String.Empty"/>.</para>
        /// </summary>
        public string EncryptionPassword { get; set; }

        /// <summary>
        /// Gets or sets keys name and value delimiter character.
        /// </summary>
        public char KeyDelimiter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether keys with same name are allowed, disallowed or ignored.
        /// </summary>
        public IniEnums KeyDuplicate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether keys name are case sensitive.
        /// </summary>
        public bool KeyNameCaseSensitive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether space is written around the keys delimiter.
        /// </summary>
        public bool KeySpaceAroundDelimiter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether sections with same name are allowed, disallowed or ignored.
        /// </summary>
        public IniEnums SectionDuplicate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether sections name are case sensitive.
        /// </summary>
        public bool SectionNameCaseSensitive { get; set; }

        /// <summary>
        /// Gets or sets wrapper characters of sections name.
        /// </summary>
        public Tuple<char,char> SectionWrapper
        {
            get => _sectionWrapper;
            set => _sectionWrapper = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IniOptions"/> class.
        /// </summary>
        public IniOptions()
        {
            _encoding = Encoding.ASCII;
            CommentStarter = new List<char>() { IniCommentStarter.Semicolon };
            Compression = false;
            EncryptionPassword = null;
            KeyDelimiter = IniKeyDelimiter.Equal;
            KeyDuplicate = IniEnums.Allowed;
            KeyNameCaseSensitive = false;
            KeySpaceAroundDelimiter = false;
            SectionDuplicate = IniEnums.Allowed;
            SectionNameCaseSensitive = false;
            SectionWrapper = IniSectionWrapper.SquareBrackets;
        }

        // Deep copy constructor.
        internal IniOptions( IniOptions options )
        {
            _encoding = options._encoding;
            CommentStarter = options.CommentStarter;
            Compression = options.Compression;
            EncryptionPassword = options.EncryptionPassword;
            KeyDelimiter = options.KeyDelimiter;
            KeyDuplicate = options.KeyDuplicate;
            KeyNameCaseSensitive = options.KeyNameCaseSensitive;
            KeySpaceAroundDelimiter = options.KeySpaceAroundDelimiter;
            SectionDuplicate = options.SectionDuplicate;
            SectionNameCaseSensitive = options.SectionNameCaseSensitive;
            SectionWrapper = options.SectionWrapper;
        }

        /* REMARKS: ToChar(bool) extension method on IniSectionWrapper would be nice, but in order to define
         *          an extension method in .NET 2.0 we need to declare ExtensionAttribute our self.
         *          
         *          Changing SectionWrapperToCharacters method's return type to Tuple<char, char> would be nice,
         *          but writing our own Tuple implementation for .NET 2.0 is an unnecessary overhead. */

    }
}
