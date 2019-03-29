using System.Collections.Generic;
using System.Linq;

namespace CK.IniFile
{
    public class IniFormat
    {
        public List<char> CommentChar { get; private set; }
        public bool SupportSection => SectionWrapper?.Any() ?? false;
        public char KeyDelimiter { get; private set; }
        public List<(char, char)> SectionWrapper { get; private set; }
        public IniDuplication Duplication { get; private set; }
        public static IniFormat NpmrcFormat => new IniFormat()
        {
            CommentChar = new List<char>( ";#" ),
            KeyDelimiter = '=',
            Duplication = IniDuplication.Ignored
        };
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
